using System.Diagnostics;
using System.Text.Json;
using Dawwerli.Application.Abstractions;
using Dawwerli.Domain.Entities;
using Dawwerli.Domain.Rules;
using Dawwerli.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using static Dawwerli.Infrastructure.Persistence.LiveSeed.LiveSeedData;

namespace Dawwerli.Infrastructure.Persistence.LiveSeed;

/// <summary>Outcome of a live seed run.</summary>
public sealed record LiveSeedReport(bool Skipped, string Message, IReadOnlyList<KeyValuePair<string, int>> Wiped,
    IReadOnlyList<KeyValuePair<string, int>> Created, TimeSpan Elapsed);

/// <summary>
/// Replaces demo data with a realistic, fictional Iraqi data set: pages, products, customers, offers, store orders,
/// follows, reviews, featured pages and ads. Runs in one transaction under a Postgres advisory lock and writes a marker
/// row in <c>settings</c>, so the startup path (<c>Seed:Live=true</c>) runs once and later starts skip it.
/// Admin users, audit logs, business settings and the reference catalog are never touched.
/// </summary>
public sealed class LiveDataSeeder(AppDbContext db, IPhoneProtector phones, IPasswordHasher passwords, IClock clock,
    IOptions<LiveSeedOptions> options, IOptions<RegionOptions> region, ILogger<LiveDataSeeder> log)
{
    /// <summary>Settings row that records a finished live seed (business settings use id 1).</summary>
    public const int MarkerId = 9001;
    public const string MarkerName = "live-seed-v1";
    /// <summary>pg_advisory_xact_lock key, so two app instances never seed at the same time.</summary>
    public const long LockKey = 0x64617777_6C697665; // "dawwlive"
    public const string SellerEmailDomain = "sellers.dawwerli.test";

    private AppDbContext Db => db;
    private IPhoneProtector Phones => phones;
    private IPasswordHasher Passwords => passwords;
    private IClock Clock => clock;

    /// <summary>Tables emptied by the wipe, children first. Statement, label.</summary>
    public static readonly (string Label, string Sql)[] WipeSteps =
    [
        ("ad_events", "DELETE FROM ad_events"),
        ("ad_campaigns", "DELETE FROM ad_campaigns"),
        ("cart_items", "DELETE FROM cart_items"),
        ("product_favorites", "DELETE FROM product_favorites"),
        ("product_ratings", "DELETE FROM product_ratings"),
        ("courier_trackings", "DELETE FROM courier_trackings"),
        ("store_order_events", "DELETE FROM store_order_events"),
        ("store_order_items", "DELETE FROM store_order_items"),
        ("store_orders", "DELETE FROM store_orders"),
        ("couriers", "DELETE FROM couriers"),
        ("products", "DELETE FROM products"),
        ("seller_accounts", "DELETE FROM seller_accounts"),
        ("ratings", "DELETE FROM ratings"),
        ("disputes", "DELETE FROM disputes"),
        ("reports", "DELETE FROM reports"),
        ("ledger_entries", "DELETE FROM ledger_entries"),
        ("top_ups", "DELETE FROM top_ups"),
        ("orders", "DELETE FROM orders"),
        ("offers", "DELETE FROM offers"),
        ("request_dispatches", "DELETE FROM request_dispatches"),
        ("requests", "DELETE FROM requests"),
        ("page_follows", "DELETE FROM page_follows"),
        ("page_marks", "DELETE FROM page_marks"),
        ("page_categories", "DELETE FROM page_categories"),
        ("page_governorates", "DELETE FROM page_governorates"),
        ("pages", "DELETE FROM pages"),
        ("strikes", "DELETE FROM strikes"),
        ("blocks", "DELETE FROM blocks"),
        ("support_messages", "DELETE FROM support_messages WHERE user_id NOT IN (SELECT id FROM users WHERE role = 2)"),
        ("notifications", "DELETE FROM notifications WHERE user_id NOT IN (SELECT id FROM users WHERE role = 2)"),
        ("otp_challenges", "DELETE FROM otp_challenges"),
        ("idempotency_records", "DELETE FROM idempotency_records WHERE user_id NOT IN (SELECT id FROM users WHERE role = 2)"),
        ("devices", "DELETE FROM devices WHERE user_id NOT IN (SELECT id FROM users WHERE role = 2)"),
        ("refresh_tokens", "DELETE FROM refresh_tokens WHERE user_id NOT IN (SELECT id FROM users WHERE role = 2)"),
        ("users (non-admin)", "DELETE FROM users WHERE role <> 2"),
        ("settings (live marker)", $"DELETE FROM settings WHERE id = {MarkerId}"),
    ];

    /// <summary>Tables the wipe keeps as they are (admins live in <c>users</c>).</summary>
    public static readonly string[] KeptTables =
        ["__EFMigrationsHistory", "audit_logs", "banned_keywords", "categories", "governorate_adjacency", "governorates", "settings", "users"];

    public Task<bool> MarkerExistsAsync(CancellationToken ct = default) => db.Settings.AsNoTracking().AnyAsync(s => s.Id == MarkerId, ct);

    /// <summary>Startup path (Seed:Live=true): wipe + seed with the configured defaults unless the marker is already there.</summary>
    public async Task<LiveSeedReport> RunOnStartupAsync(CancellationToken ct = default)
    {
        if (await MarkerExistsAsync(ct))
        {
            log.LogInformation("Live seed: marker {Marker} found, skipping. Remove Seed__Live from the server environment.", MarkerName);
            return new LiveSeedReport(true, "marker present", [], [], TimeSpan.Zero);
        }
        log.LogWarning("Live seed: Seed:Live=true and no marker {Marker}. DELETING all non-admin data and seeding live data now.", MarkerName);
        var report = await RunAsync(options.Value.Copy(), wipe: true, onlyIfNoMarker: true, ct);
        if (report.Skipped) log.LogInformation("Live seed: {Message}", report.Message);
        else
        {
            log.LogWarning("Live seed: wiped {Total} rows: {Wiped}", report.Wiped.Sum(kv => kv.Value),
                string.Join(", ", report.Wiped.Where(kv => kv.Value > 0).Select(kv => $"{kv.Key}={kv.Value}")));
            log.LogWarning("Live seed: done in {Seconds:F1}s: {Counts}. Remove Seed__Live from the server environment.",
                report.Elapsed.TotalSeconds, string.Join(", ", report.Created.Select(kv => $"{kv.Key}={kv.Value}")));
        }
        return report;
    }

    /// <summary>
    /// Wipes (when asked) and seeds in one transaction under the advisory lock, then writes the marker.
    /// <paramref name="onlyIfNoMarker"/> re-checks the marker inside the lock (startup); the command uses <paramref name="wipe"/>.
    /// Without <paramref name="wipe"/> it refuses when the marker or any page already exists.
    /// </summary>
    public async Task<LiveSeedReport> RunAsync(LiveSeedOptions o, bool wipe, bool onlyIfNoMarker, CancellationToken ct = default)
    {
        if (o.Validate() is { } bad) throw new ArgumentException(bad);
        var sw = Stopwatch.StartNew();
        var timeout = db.Database.GetCommandTimeout();
        db.Database.SetCommandTimeout(TimeSpan.FromMinutes(10));
        var autoDetect = db.ChangeTracker.AutoDetectChangesEnabled;
        db.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_xact_lock({LockKey})", ct);
            if (onlyIfNoMarker && await MarkerExistsAsync(ct))
                return new LiveSeedReport(true, "marker written by another instance, skipping", [], [], sw.Elapsed);
            if (!wipe && (await MarkerExistsAsync(ct) || await db.Pages.AnyAsync(ct)))
                return new LiveSeedReport(true, "data already present; run with --wipe to replace it", [], [], sw.Elapsed);

            var wiped = wipe ? await WipeAsync(ct) : [];
            var created = await new Run(this, o).ExecuteAsync(ct);
            db.Settings.Add(new SettingsRecord
            {
                Id = MarkerId, UpdatedAt = clock.UtcNow,
                Data = JsonSerializer.Serialize(new { marker = MarkerName, seededAt = clock.UtcNow, randomSeed = o.RandomSeed, counts = created.ToDictionary() }),
            });
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            await tx.CommitAsync(ct);
            return new LiveSeedReport(false, "seeded", wiped, created, sw.Elapsed);
        }
        finally
        {
            db.ChangeTracker.Clear();
            db.ChangeTracker.AutoDetectChangesEnabled = autoDetect;
            db.Database.SetCommandTimeout(timeout);
        }
    }

    /// <summary>Deletes every non-admin row, children first. Must run inside the caller's transaction.</summary>
    public async Task<List<KeyValuePair<string, int>>> WipeAsync(CancellationToken ct = default)
    {
        var counts = new List<KeyValuePair<string, int>>();
        foreach (var (label, sql) in WipeSteps)
            counts.Add(new(label, await db.Database.ExecuteSqlRawAsync(sql, ct)));
        return counts;
    }

    /// <summary>Read-only preview for --dry-run: current row counts and what would be created.</summary>
    public async Task<string> DescribeAsync(LiveSeedOptions o, CancellationToken ct = default)
    {
        var lines = new List<string>
        {
            $"marker {MarkerName}: {(await MarkerExistsAsync(ct) ? "present" : "absent")}",
            $"governorates: {await db.Governorates.CountAsync(ct)}, categories: {await db.Categories.CountAsync(ct)}",
            $"current: users(non-admin)={await db.Users.CountAsync(u => u.Role != UserRole.Admin, ct)}, admins={await db.Users.CountAsync(u => u.Role == UserRole.Admin, ct)}, " +
            $"pages={await db.Pages.CountAsync(ct)}, products={await db.Products.CountAsync(ct)}, requests={await db.Requests.CountAsync(ct)}, " +
            $"offers={await db.Offers.CountAsync(ct)}, store_orders={await db.StoreOrders.CountAsync(ct)}",
            $"plan: pages={o.Pages} (products on first {o.ProductPages}, {o.MinProductsPerPage}-{o.MaxProductsPerPage} each), customers={o.Customers}, " +
            $"offers={o.Offers}, store_orders≈{o.StoreOrders}, banners={o.Banners}, featured_ads={o.FeaturedAds}, featured_pages={o.FeaturedPages}, " +
            $"history={o.HistoryDays} days, seed={o.RandomSeed}, seller_accounts={(string.IsNullOrEmpty(o.SellerPassword) ? "no" : "yes")}",
        };
        return string.Join(Environment.NewLine, lines);
    }

    private TimeZoneInfo Zone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(region.Value.TimeZone) ? "Asia/Baghdad" : region.Value.TimeZone); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.CreateCustomTimeZone("Baghdad", TimeSpan.FromHours(3), "Baghdad", "Baghdad"); }
    }

    /// <summary>One generation pass. Builds everything in memory (a few thousand rows), then inserts in FK order in batches.</summary>
    private sealed class Run(LiveDataSeeder owner, LiveSeedOptions o)
    {
        private readonly AppDbContext db = owner.Db;
        private readonly Random rng = new(o.RandomSeed);
        private readonly DateTime now = owner.Clock.UtcNow;
        private readonly TimeZoneInfo tz = owner.Zone();
        private BusinessRules rules = new();
        private Dictionary<string, int> gov = [];
        private Dictionary<string, int> cat = [];
        private Dictionary<int, int[]> neighbours = [];
        private readonly Dictionary<int, string> govName = [];

        private readonly List<User> users = [];
        private readonly List<Page> pages = [];
        private readonly List<PageCategory> pageCats = [];
        private readonly List<PageGovernorate> pageGovs = [];
        private readonly List<Courier> couriers = [];
        private readonly List<Product> products = [];
        private readonly List<PageFollow> follows = [];
        private readonly List<ProductFavorite> favorites = [];
        private readonly List<StoreOrder> storeOrders = [];
        private readonly List<ProductRating> productRatings = [];
        private readonly List<Request> requests = [];
        private readonly List<RequestDispatch> dispatches = [];
        private readonly List<Offer> offers = [];
        private readonly List<Order> orders = [];
        private readonly List<Rating> ratings = [];
        private readonly List<LedgerEntry> ledger = [];
        private readonly List<AdCampaign> ads = [];
        private readonly List<Notification> notes = [];
        private readonly List<SellerAccount> sellers = [];

        private readonly List<User> customers = [];
        private readonly Dictionary<Guid, Place> placeOf = [];
        private readonly Dictionary<Guid, CategoryKit> kitOf = [];
        private readonly Dictionary<Guid, User> ownerOf = [];
        private readonly Dictionary<Guid, List<Product>> productsOf = [];
        private readonly Dictionary<Guid, List<Courier>> couriersOf = [];
        private readonly Dictionary<Guid, ProductSeed> templateOf = [];

        public async Task<List<KeyValuePair<string, int>>> ExecuteAsync(CancellationToken ct)
        {
            await LoadReferenceAsync(ct);
            Customers();
            Pages();
            Couriers();
            Products();
            FollowsAndFavorites();
            await StoreOrdersAsync(ct);
            await RequestsAndOffersAsync(ct);
            FeaturedAndAds();
            Balances();
            SellerAccounts();

            await InsertAsync(users, ct);
            await InsertAsync(pages, ct);
            await InsertAsync(pageCats, ct);
            await InsertAsync(pageGovs, ct);
            await InsertAsync(couriers, ct);
            await InsertAsync(products, ct);
            await InsertAsync(follows, ct);
            await InsertAsync(favorites, ct);
            await InsertAsync(storeOrders, ct, 200);
            await InsertAsync(productRatings, ct);
            await InsertAsync(requests, ct);
            await InsertAsync(dispatches, ct);
            await InsertAsync(offers, ct);
            await InsertAsync(orders, ct);
            await InsertAsync(ratings, ct);
            await InsertAsync(ledger, ct);
            await InsertAsync(ads, ct);
            await InsertAsync(notes, ct);
            await InsertAsync(sellers, ct);
            await AlignTimestampsAsync(ct);

            return
            [
                new("customers", customers.Count), new("page_users", pages.Count), new("pages", pages.Count),
                new("pages_verified", pages.Count(p => p.Status == PageStatus.Verified)), new("page_categories", pageCats.Count),
                new("page_governorates", pageGovs.Count), new("couriers", couriers.Count), new("products", products.Count),
                new("page_follows", follows.Count), new("product_favorites", favorites.Count), new("store_orders", storeOrders.Count),
                new("store_order_items", storeOrders.Sum(s => s.Items.Count)), new("store_order_events", storeOrders.Sum(s => s.History.Count)),
                new("product_ratings", productRatings.Count), new("requests", requests.Count), new("request_dispatches", dispatches.Count),
                new("offers", offers.Count), new("orders", orders.Count), new("ratings", ratings.Count), new("ledger_entries", ledger.Count),
                new("ad_campaigns", ads.Count), new("featured_pages", pages.Count(p => p.FeaturedOrder != null)),
                new("notifications", notes.Count), new("seller_accounts", sellers.Count),
            ];
        }

        // ---------- helpers ----------

        private Guid NewId()
        {
            Span<byte> b = stackalloc byte[16];
            rng.NextBytes(b);
            b[7] = (byte)((b[7] & 0x0F) | 0x40);
            b[8] = (byte)((b[8] & 0x3F) | 0x80);
            return new Guid(b);
        }

        private int Int(int min, int max) => rng.Next(min, max + 1);
        private bool Chance(double p) => rng.NextDouble() < p;
        private T Pick<T>(IReadOnlyList<T> list) => list[rng.Next(list.Count)];
        private DateTime Between(DateTime from, DateTime to) => to <= from ? from : from.AddSeconds(rng.NextDouble() * (to - from).TotalSeconds);
        private DateTime DaysAgo(double days) => now.AddDays(-days);
        private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
        private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;

        private List<T> Sample<T>(IReadOnlyList<T> list, int n)
        {
            var copy = list.ToList();
            for (var i = copy.Count - 1; i > 0; i--) { var j = rng.Next(i + 1); (copy[i], copy[j]) = (copy[j], copy[i]); }
            return copy.Take(Math.Min(n, copy.Count)).ToList();
        }

        private Place WeightedPlace()
        {
            var total = Places.Sum(p => p.Weight);
            var x = rng.Next(total);
            foreach (var p in Places) { if (x < p.Weight) return p; x -= p.Weight; }
            return Places[0];
        }

        private static long RoundPrice(double p) => p switch
        {
            < 20_000 => (long)Math.Round(p / 250) * 250,
            < 100_000 => (long)Math.Round(p / 500) * 500,
            < 500_000 => (long)Math.Round(p / 1000) * 1000,
            _ => (long)Math.Round(p / 5000) * 5000,
        };

        private long Price(long min, long max) => RoundPrice(min + rng.NextDouble() * (max - min));
        private string[] Images(string tag, int n) => Sample(LiveSeedImages.ByTag[tag], n).ToArray();
        private string Image(string tag) => Pick(LiveSeedImages.ByTag[tag]);

        private (double Lat, double Lng) Point(Place p) =>
            (Math.Round(p.LatMin + rng.NextDouble() * (p.LatMax - p.LatMin), 6), Math.Round(p.LngMin + rng.NextDouble() * (p.LngMax - p.LngMin), 6));

        private string PersonName() => $"{(Chance(0.6) ? Pick(MaleNames) : Pick(FemaleNames))} {Pick(FamilyNames)}";
        private string Stamp(Page page, DateTime at) => $"{page.Name} · {TimeZoneInfo.ConvertTimeFromUtc(at, tz):yyyy-MM-dd HH:mm}";
        private static string OrderNo(long n) => $"DW-{n}";

        private void Notify(Guid userId, string title, string body, string route, DateTime at, bool read)
        {
            DateTime? readAt = read ? at.AddMinutes(Int(2, 600)) : null;
            if (readAt > now) readAt = null;
            notes.Add(new Notification { Id = NewId(), UserId = userId, Title = title, Body = body, Route = route, CreatedAt = at, ReadAt = readAt });
        }

        private readonly Queue<long> orderNumbers = new();
        private readonly Queue<long> requestNumbers = new();

        private async Task<long> NextAsync(string seq, Queue<long> pool, CancellationToken ct)
        {
            if (pool.Count == 0)
            {
                var name = seq == AppDbContext.OrderNumberSeq ? AppDbContext.OrderNumberSeq : AppDbContext.RequestNumberSeq;
                foreach (var n in await db.Database.SqlQueryRaw<long>($"SELECT nextval('{name}') AS \"Value\" FROM generate_series(1, 100)").ToListAsync(ct))
                    pool.Enqueue(n);
            }
            return pool.Dequeue();
        }

        private async Task InsertAsync<T>(List<T> rows, CancellationToken ct, int batch = 1000) where T : class
        {
            for (var i = 0; i < rows.Count; i += batch)
            {
                db.Set<T>().AddRange(rows.Skip(i).Take(batch));
                await db.SaveChangesAsync(ct);
                db.ChangeTracker.Clear();
            }
        }

        // ---------- reference ----------

        private async Task LoadReferenceAsync(CancellationToken ct)
        {
            gov = await db.Governorates.AsNoTracking().ToDictionaryAsync(g => g.Name, g => g.Id, ct);
            cat = await db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Slug, c => c.Id, ct);
            foreach (var (name, id) in gov) govName[id] = name;
            neighbours = (await db.GovernorateAdjacency.AsNoTracking().ToListAsync(ct))
                .GroupBy(a => a.GovernorateId).ToDictionary(g => g.Key, g => g.Select(a => a.NeighborId).ToArray());
            var missingGov = Places.Where(p => !gov.ContainsKey(p.Governorate)).Select(p => p.Governorate).ToList();
            var missingCat = Kits.Where(k => !cat.ContainsKey(k.Slug)).Select(k => k.Slug).ToList();
            if (missingGov.Count > 0 || missingCat.Count > 0)
                throw new InvalidOperationException($"Reference catalog is missing governorates [{string.Join(", ", missingGov)}] or categories [{string.Join(", ", missingCat)}]; start the app once so it seeds them.");
            var row = await db.Settings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == RulesProvider.SettingsId, ct);
            if (row is not null) rules = JsonSerializer.Deserialize<BusinessRules>(row.Data) ?? new BusinessRules();
        }

        // ---------- people and pages ----------

        private void Customers()
        {
            for (var i = 1; i <= o.Customers; i++)
            {
                var place = WeightedPlace();
                var phone = $"0790001{i:D4}";
                var u = new User
                {
                    Id = NewId(), Role = UserRole.Customer, PhoneHash = owner.Phones.Hash(phone), PhoneEncrypted = owner.Phones.Encrypt(phone),
                    Name = PersonName(), GovernorateId = gov[place.Governorate], Area = Pick(place.Areas),
                    CreatedAt = Between(DaysAgo(o.HistoryDays), DaysAgo(2)),
                };
                users.Add(u);
                customers.Add(u);
                placeOf[u.Id] = place;
            }
        }

        /// <summary>Category mix of the page set: phones, clothes and shoes are the most common shops.</summary>
        private static readonly (string Slug, int Weight)[] KitWeights =
            [("phones", 18), ("clothes", 18), ("shoes", 13), ("perfumes", 10), ("watches", 7), ("care", 9), ("home", 10), ("electronics", 9), ("gifts", 6)];

        private CategoryKit WeightedKit()
        {
            var x = rng.Next(KitWeights.Sum(k => k.Weight));
            foreach (var (slug, w) in KitWeights) { if (x < w) return Kits.First(k => k.Slug == slug); x -= w; }
            return Kits[0];
        }

        private void Pages()
        {
            var handles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var names = new HashSet<string>();
            for (var i = 1; i <= o.Pages; i++)
            {
                var kit = WeightedKit();
                var place = WeightedPlace();
                var word = Pick(ShopWords);
                var name = $"{Pick(kit.ShopPrefixes)} {word.Ar}";
                if (!names.Add(name)) { name = $"{name} - {place.Governorate}"; if (!names.Add(name)) { name = $"{name} {i}"; names.Add(name); } }
                var handle = $"{word.Lat}.{kit.HandleTag}";
                if (!handles.Add(handle))
                {
                    var alt = $"{word.Lat}.{kit.HandleTag}.iq";
                    var n = 2;
                    while (!handles.Add(alt)) alt = $"{word.Lat}{n++}.{kit.HandleTag}";
                    handle = alt;
                }

                var hasProducts = i <= o.ProductPages;
                var created = hasProducts ? Between(DaysAgo(o.HistoryDays), DaysAgo(o.HistoryDays * 0.35)) : Between(DaysAgo(o.HistoryDays), DaysAgo(1));
                var phone = $"0790002{i:D4}";
                var govId = gov[place.Governorate];
                var user = new User
                {
                    Id = NewId(), Role = UserRole.Page, PhoneHash = owner.Phones.Hash(phone), PhoneEncrypted = owner.Phones.Encrypt(phone),
                    Name = name, GovernorateId = govId, Area = Pick(place.Areas), CreatedAt = created.AddMinutes(-Int(5, 90)),
                };
                var status = hasProducts ? PageStatus.Verified : rng.NextDouble() switch
                {
                    < 0.92 => PageStatus.Verified, < 0.96 => PageStatus.Pending, < 0.98 => PageStatus.Suspended, _ => PageStatus.Rejected,
                };
                var page = new Page
                {
                    Id = NewId(), UserId = user.Id, Name = name, InstagramHandle = handle, Bio = Pick(kit.Bios), ImageKey = Image(Pick(kit.AvatarTags)),
                    Status = status, BioCode = $"DW{Int(100000, 999999)}", Receiving = status == PageStatus.Verified && Chance(0.95),
                    CreatedAt = created,
                    RejectReason = status == PageStatus.Rejected ? "رابط الانستغرام ما يطابق اسم الصفحة" : null,
                    SuspendedUntil = status == PageStatus.Suspended ? now.AddDays(Int(2, 10)) : null,
                };
                users.Add(user);
                pages.Add(page);
                ownerOf[page.Id] = user;
                kitOf[page.Id] = kit;
                placeOf[page.Id] = place;

                pageCats.Add(new PageCategory { PageId = page.Id, CategoryId = cat[kit.Slug] });
                if (Chance(0.3))
                {
                    var extra = Pick(RelatedCategories[kit.Slug]);
                    pageCats.Add(new PageCategory { PageId = page.Id, CategoryId = cat[extra] });
                }
                pageGovs.Add(new PageGovernorate { PageId = page.Id, GovernorateId = govId });
                if (Chance(0.25) && neighbours.TryGetValue(govId, out var near) && near.Length > 0)
                    foreach (var n in Sample(near, Int(1, 2)))
                        pageGovs.Add(new PageGovernorate { PageId = page.Id, GovernorateId = n });
            }
        }

        private IEnumerable<Page> ProductPages => pages.Take(o.ProductPages);

        private void Couriers()
        {
            var n = 1;
            foreach (var page in ProductPages)
            {
                var list = new List<Courier>();
                for (var k = Int(1, 2); k > 0; k--)
                    list.Add(new Courier
                    {
                        Id = NewId(), PageId = page.Id, Name = $"{Pick(MaleNames)} {Pick(FamilyNames)}", Phone = $"0790003{n++:D4}",
                        Active = true, CreatedAt = page.CreatedAt.AddDays(Int(1, 10)),
                    });
                couriers.AddRange(list);
                couriersOf[page.Id] = list;
            }
        }

        /// <summary>Extra spec lines for templates without variants, so a page can list 10–20 distinct items.</summary>
        private static readonly Dictionary<string, string[]> FallbackVariants = new()
        {
            ["watches"] = ["فضي", "ذهبي", "أسود"], ["care"] = ["حجم صغير", "حجم كبير"], ["gifts"] = ["حجم وسط", "حجم كبير"],
            ["perfumes"] = ["تستر", "نسخة هدية"], ["home"] = ["لون أبيض", "لون رصاصي"], ["electronics"] = ["أسود", "أبيض"],
            ["phones"] = ["أسود", "أبيض"], ["shoes"] = ["أسود", "أبيض"], ["clothes"] = ["أسود", "كحلي"],
        };

        private void Products()
        {
            foreach (var page in ProductPages)
            {
                var kit = kitOf[page.Id];
                var want = Int(o.MinProductsPerPage, o.MaxProductsPerPage);
                var candidates = new List<(ProductSeed Seed, string Name, bool Used)>();
                foreach (var t in kit.Products)
                {
                    if (t.Variants is { Length: > 0 }) candidates.AddRange(t.Variants.Select(v => (t, $"{t.Name} {v}", false)));
                    else candidates.Add((t, t.Name, false));
                    if (t.CanBeUsed) candidates.Add((t, $"{t.Name} مستعمل", true));
                }
                if (candidates.Count < want)
                    foreach (var t in kit.Products.Where(t => t.Variants is not { Length: > 0 }))
                        candidates.AddRange(FallbackVariants[kit.Slug].Select(v => (t, $"{t.Name} - {v}", false)));

                var list = new List<Product>();
                foreach (var (seed, name, used) in Sample(candidates, want))
                {
                    var price = used ? RoundPrice(Price(seed.MinPrice, seed.MaxPrice) * (0.6 + rng.NextDouble() * 0.15)) : Price(seed.MinPrice, seed.MaxPrice);
                    var condition = used ? "الحالة: مستعمل نظيف، مفحوص ومضمون." : "الحالة: جديد بالكارتون.";
                    var p = new Product
                    {
                        Id = NewId(), PageId = page.Id, Name = name, Description = $"{seed.Description}\n{condition}\nالسعر بالدينار العراقي، التوصيل متوفر.",
                        Price = price, ImageKeys = Images(seed.Tag, Int(1, 3)), CategoryId = cat[kit.Slug],
                        Stock = Chance(0.08) ? 0 : Int(1, 25), Active = Chance(0.95),
                        CreatedAt = Between(page.CreatedAt.AddDays(1), DaysAgo(1)),
                    };
                    list.Add(p);
                    templateOf[p.Id] = seed;
                }
                products.AddRange(list);
                productsOf[page.Id] = list;
            }
        }

        private List<Page> LivePages => pages.Where(p => p.IsActive(now)).ToList();

        private void FollowsAndFavorites()
        {
            var live = LivePages;
            var withProducts = live.Where(p => productsOf.ContainsKey(p.Id)).ToList();
            var visible = products.Where(p => p.Visible).ToList();
            foreach (var c in customers)
            {
                var chosen = new HashSet<Guid>();
                for (var k = Int(2, 8); k > 0; k--)
                {
                    var p = Chance(0.7) && withProducts.Count > 0 ? Pick(withProducts) : Pick(live);
                    if (!chosen.Add(p.Id)) continue;
                    follows.Add(new PageFollow { Id = NewId(), UserId = c.Id, PageId = p.Id, CreatedAt = Between(Max(c.CreatedAt, p.CreatedAt), now.AddHours(-1)) });
                }
                var favs = new HashSet<Guid>();
                for (var k = Int(0, 6); k > 0 && visible.Count > 0; k--)
                {
                    var pr = Pick(visible);
                    if (!favs.Add(pr.Id)) continue;
                    favorites.Add(new ProductFavorite { Id = NewId(), UserId = c.Id, ProductId = pr.Id, CreatedAt = Between(Max(c.CreatedAt, pr.CreatedAt), now.AddHours(-1)) });
                }
            }
        }

        // ---------- store orders ----------

        private string Address(Place place, User customer) => $"{place.Governorate} - {customer.Area} - {Pick(Streets)}";

        private StoreOrder NewStoreOrder(long number, User customer, Page page, StoreOrderKind kind, long subtotal, DateTime at)
        {
            var place = placeOf[customer.Id];
            var (lat, lng) = Point(place);
            var pct = rules.StoreCommissionPercent;
            return new StoreOrder
            {
                Id = NewId(), Number = number, CustomerUserId = customer.Id, PageId = page.Id, Kind = kind,
                PaymentMethod = PaymentMethod.Cash, PaymentStatus = PaymentStatus.CashOnDelivery,
                Address = Address(place, customer), GovernorateId = customer.GovernorateId, Latitude = lat, Longitude = lng,
                LocationNote = Pick(LocationNotes), Subtotal = subtotal, CommissionPercent = pct, Commission = StoreOrderFlow.CommissionOf(subtotal, pct),
                CreatedAt = at,
            };
        }

        private void SendOut(StoreOrder so, Page page, DateTime at)
        {
            if (couriersOf.TryGetValue(page.Id, out var list) && list.Count > 0)
            {
                var c = Pick(list);
                so.CourierId = c.Id;
                so.CourierName = c.Name;
                so.CourierPhone = c.Phone;
            }
            so.Record(StoreOrderStatus.OutForDelivery, at);
        }

        private static readonly (StoreOrderStatus Status, double Share)[] StoreMix =
        [
            (StoreOrderStatus.Delivered, 0.50), (StoreOrderStatus.OutForDelivery, 0.10), (StoreOrderStatus.Accepted, 0.10),
            (StoreOrderStatus.New, 0.12), (StoreOrderStatus.Cancelled, 0.10), (StoreOrderStatus.Rejected, 0.08),
        ];

        private StoreOrderStatus StoreStatus()
        {
            var x = rng.NextDouble();
            foreach (var (s, share) in StoreMix) { if (x < share) return s; x -= share; }
            return StoreOrderStatus.Delivered;
        }

        private async Task StoreOrdersAsync(CancellationToken ct)
        {
            var shops = ProductPages.Where(p => p.IsActive(now) && productsOf[p.Id].Any(x => x.Visible)).ToList();
            if (shops.Count == 0 || customers.Count == 0) return;
            var reviewed = new HashSet<(Guid, Guid)>();
            var ratingSums = new Dictionary<Guid, (int Sum, int Count)>();

            for (var i = 0; i < o.StoreOrders; i++)
            {
                var status = StoreStatus();
                var customer = Pick(customers);
                // Prefer shops that deliver to the customer's governorate.
                var local = shops.Where(p => pageGovs.Any(g => g.PageId == p.Id && g.GovernorateId == customer.GovernorateId)).ToList();
                var page = local.Count > 0 && Chance(0.8) ? Pick(local) : Pick(shops);
                var lines = Sample(productsOf[page.Id].Where(x => x.Visible).ToList(), Chance(0.75) ? 1 : Int(2, 3));
                var earliest = Max(Max(customer.CreatedAt, page.CreatedAt), lines.Max(l => l.CreatedAt)).AddHours(1);
                var t0 = status switch
                {
                    StoreOrderStatus.New => Between(Max(earliest, now.AddHours(-30)), now.AddMinutes(-10)),
                    StoreOrderStatus.Accepted => Between(Max(earliest, now.AddDays(-3)), now.AddHours(-2)),
                    StoreOrderStatus.OutForDelivery => Between(Max(earliest, now.AddDays(-3)), now.AddHours(-8)),
                    _ => Between(earliest, now.AddDays(-3)),
                };
                if (t0 > now.AddMinutes(-10)) t0 = now.AddMinutes(-Int(10, 120));

                var items = lines.Select(p => new StoreOrderItem
                {
                    Id = NewId(), ProductId = p.Id, Name = p.Name, UnitPrice = p.Price, Quantity = Chance(0.85) ? 1 : 2, ImageKey = p.ImageKeys.FirstOrDefault(),
                }).ToList();
                var so = NewStoreOrder(await NextAsync(AppDbContext.OrderNumberSeq, orderNumbers, ct), customer, page, StoreOrderKind.Store,
                    items.Sum(x => x.UnitPrice * x.Quantity), t0);
                foreach (var it in items) { it.OrderId = so.Id; so.Items.Add(it); }
                if (Chance(0.2)) so.Note = Pick<string>(["رجاءً اتصل قبل التوصيل", "التوصيل بعد العصر", "تغليف هدية لو سمحت", "لا تطرق الباب، اتصل"]);
                so.Record(StoreOrderStatus.New, t0);

                var t = t0;
                switch (status)
                {
                    case StoreOrderStatus.Cancelled:
                        so.StatusReason = "العميل ألغى الطلب";
                        so.Record(StoreOrderStatus.Cancelled, t.AddMinutes(Int(5, 90)));
                        break;
                    case StoreOrderStatus.Rejected:
                        so.StatusReason = Pick<string>(["المنتج نفد من المخزن", "ما نوصل لهذه المنطقة حالياً", "السعر تغيّر"]);
                        so.Record(StoreOrderStatus.Rejected, t.AddMinutes(Int(10, 240)));
                        break;
                    case StoreOrderStatus.New:
                        break;
                    default:
                        t = t.AddMinutes(Int(10, 180));
                        so.Record(StoreOrderStatus.Accepted, Min(t, now.AddMinutes(-5)));
                        if (status == StoreOrderStatus.Accepted) break;
                        t = t.AddHours(Int(2, 20));
                        SendOut(so, page, Min(t, now.AddMinutes(-3)));
                        if (status == StoreOrderStatus.OutForDelivery) break;
                        t = t.AddHours(Int(1, 24));
                        so.Record(StoreOrderStatus.Delivered, Min(t, now.AddMinutes(-1)));
                        break;
                }
                storeOrders.Add(so);

                // Notifications: the seller hears about the order, the customer about its latest step.
                var seller = ownerOf[page.Id];
                Notify(seller.Id, "طلب متجر جديد", $"طلب {OrderNo(so.Number)} — {so.Subtotal:N0} د.ع", $"/seller/store/orders/{so.Id}", t0, so.Status != StoreOrderStatus.New);
                if (so.Status != StoreOrderStatus.New)
                    Notify(customer.Id, $"تحديث طلبك {OrderNo(so.Number)}", StatusText(so.Status), $"/store/orders/{so.Id}", so.StatusChangedAt, Chance(0.7));

                // Reviews: only for delivered orders, once per customer and product.
                if (so.Status != StoreOrderStatus.Delivered) continue;
                foreach (var it in items)
                {
                    if (!Chance(0.6) || !reviewed.Add((customer.Id, it.ProductId))) continue;
                    var stars = rng.NextDouble() switch { < 0.55 => 5, < 0.83 => 4, < 0.93 => 3, < 0.97 => 2, _ => 1 };
                    var comment = stars >= 4 ? Pick(ProductReviews) : Pick<string>(["المنتج مو مثل الصورة", "التوصيل تأخر", "النوعية أقل من المتوقع", ""]);
                    productRatings.Add(new ProductRating
                    {
                        Id = NewId(), ProductId = it.ProductId, UserId = customer.Id, Stars = stars, Comment = comment,
                        CreatedAt = Between(so.StatusChangedAt.AddHours(1), Max(so.StatusChangedAt.AddHours(2), Min(so.StatusChangedAt.AddDays(3), now.AddMinutes(-1)))),
                    });
                    var (sum, count) = ratingSums.GetValueOrDefault(it.ProductId);
                    ratingSums[it.ProductId] = (sum + stars, count + 1);
                }
            }
            foreach (var p in products)
                if (ratingSums.TryGetValue(p.Id, out var agg)) { p.Rating = Math.Round((double)agg.Sum / agg.Count, 1); p.RatingCount = agg.Count; }
            foreach (var r in productRatings.Where(r => r.CreatedAt > now)) r.CreatedAt = now.AddMinutes(-1);
        }

        private static string StatusText(StoreOrderStatus s) => s switch
        {
            StoreOrderStatus.Accepted => "الصفحة قبلت طلبك وراح تجهزه",
            StoreOrderStatus.OutForDelivery => "طلبك طلع للتوصيل",
            StoreOrderStatus.Delivered => "تم توصيل طلبك، قيّم المنتج",
            StoreOrderStatus.Cancelled => "تم إلغاء الطلب",
            StoreOrderStatus.Rejected => "الصفحة اعتذرت عن الطلب",
            _ => "تم استلام طلبك",
        };

        // ---------- requests, offers, deals ----------

        private enum Plan { Open, InPurchase, Awaiting, Delivered, Rated, Expired }

        private Plan NextPlan(int index) => (index % 10) switch
        {
            0 or 5 or 8 => Plan.Open,
            1 => Plan.InPurchase,
            2 => Plan.Awaiting,
            3 or 6 => Plan.Rated,
            4 => Plan.Delivered,
            7 => Plan.Expired,
            _ => Plan.Rated,
        };

        private async Task RequestsAndOffersAsync(CancellationToken ct)
        {
            var live = LivePages;
            if (o.Offers == 0 || live.Count == 0 || customers.Count == 0) return;
            var remaining = o.Offers;
            var deals = new Dictionary<Guid, (int Deals, int Sum, int Count)>();
            for (var index = 0; remaining > 0; index++)
            {
                var plan = NextPlan(index);
                var customer = Pick(customers);
                var kit = Kits[rng.Next(Kits.Length)];
                var catId = cat[kit.Slug];
                var govId = customer.GovernorateId!.Value;
                var matching = live.Where(p => pageCats.Any(c => c.PageId == p.Id && c.CategoryId == catId)
                    && pageGovs.Any(g => g.PageId == p.Id && g.GovernorateId == govId)).ToList();
                if (matching.Count == 0) matching = live.Where(p => pageCats.Any(c => c.PageId == p.Id && c.CategoryId == catId)).ToList();
                if (matching.Count == 0) continue;

                var want = Math.Min(remaining, plan == Plan.Expired ? Int(1, 2) : Int(1, 4));
                var bidders = Sample(matching, want);
                if (bidders.Count == 0) continue;
                remaining -= bidders.Count;

                var template = Pick(kit.Products);
                var created = plan switch
                {
                    Plan.Open => Between(now.AddHours(-10), now.AddMinutes(-20)),
                    Plan.InPurchase => Between(now.AddDays(-1), now.AddHours(-6)),
                    Plan.Awaiting => Between(now.AddDays(-3), now.AddHours(-30)),
                    Plan.Expired => Between(DaysAgo(o.HistoryDays * 0.8), DaysAgo(2)),
                    _ => Between(DaysAgo(o.HistoryDays * 0.9), DaysAgo(4)),
                };
                created = Max(created, customer.CreatedAt.AddHours(1));
                if (plan == Plan.Open && created < now.AddHours(-10)) created = now.AddHours(-Int(1, 9));
                var sizes = SchemaList(kit.Slug, "sizes");
                var colors = SchemaList(kit.Slug, "colors");
                var photo = Chance(0.6);
                var condition = template.CanBeUsed && Chance(0.4) ? (Chance(0.5) ? ItemCondition.Used : ItemCondition.Any) : ItemCondition.New;
                var request = new Request
                {
                    Id = NewId(), CustomerId = customer.Id, Number = await NextAsync(AppDbContext.RequestNumberSeq, requestNumbers, ct),
                    Description = Pick(kit.RequestTexts), Mode = photo ? InputMode.Photo : InputMode.Text, MediaKey = photo ? Image(template.Tag) : null,
                    CategoryId = catId, Condition = condition, GovernorateId = govId, CreatedAt = created,
                    Fields = JsonSerializer.Serialize(new { size = sizes.Length > 0 ? Pick(sizes) : null, color = colors.Length > 0 && Chance(0.6) ? Pick(colors) : null, pageId = (string?)null }),
                    MaxPrice = RoundPrice(template.MaxPrice * (1 + rng.NextDouble() * 0.2)),
                    EarlyUntil = created, PublicDispatched = true, ExpandsAt = created.AddMinutes(15), ExpiresAt = created.AddHours(24),
                    Status = plan switch
                    {
                        Plan.Open => RequestStatus.Open, Plan.Expired => RequestStatus.Expired,
                        Plan.Delivered or Plan.Rated => RequestStatus.Closed, _ => RequestStatus.Selected,
                    },
                    OffersCount = bidders.Count,
                };
                requests.Add(request);

                foreach (var p in Sample(matching, Math.Min(matching.Count, bidders.Count + Int(1, 5))).Union(bidders))
                    dispatches.Add(new RequestDispatch { RequestId = request.Id, PageId = p.Id, DispatchedAt = created.AddMinutes(Int(0, 15)), SeenAt = Chance(0.7) ? created.AddMinutes(Int(15, 120)) : null });

                var made = new List<Offer>();
                foreach (var page in bidders)
                {
                    var at = Min(created.AddMinutes(Int(3, 180)), plan == Plan.Open ? now.AddMinutes(-2) : created.AddHours(20));
                    var price = Price(template.MinPrice, Math.Min(template.MaxPrice, request.MaxPrice));
                    var offer = new Offer
                    {
                        Id = NewId(), RequestId = request.Id, PageId = page.Id, Price = price, DeliveryHours = Pick<int>([6, 12, 24, 24, 48, 72]),
                        Condition = condition == ItemCondition.Any ? (Chance(0.5) ? ItemCondition.Used : ItemCondition.New) : condition,
                        Note = Pick(OfferNotes), PhotoKey = Image(template.Tag), PhotoStamp = Stamp(page, at), CreatedAt = at,
                        Status = plan switch { Plan.Open => OfferStatus.Active, Plan.Expired => OfferStatus.Closed, _ => OfferStatus.NotSelected },
                    };
                    made.Add(offer);
                    if (plan == Plan.Open)
                        Notify(customer.Id, "وصلك عرض جديد", $"{page.Name} عرضت {price:N0} د.ع", $"/requests/{request.Id}/offers", at, Chance(0.4));
                }
                offers.AddRange(made);
                if (plan == Plan.Open)
                {
                    foreach (var p in Sample(matching.Except(bidders).ToList(), 2))
                        Notify(ownerOf[p.Id].Id, "طلب جديد يناسبك", request.Title, $"/seller/requests/{request.Id}/offer", created, false);
                    continue;
                }
                if (plan == Plan.Expired) continue;

                // Customer picks the cheapest offer most of the time.
                var chosen = Chance(0.7) ? made.MinBy(x => x.Price)! : Pick(made);
                chosen.Status = OfferStatus.Selected;
                var page0 = pages.First(p => p.Id == chosen.PageId);
                var selectedAt = Max(made.Max(x => x.CreatedAt), created).AddMinutes(Int(10, 240));
                if (selectedAt > now.AddMinutes(-30)) selectedAt = now.AddMinutes(-30);
                var order = new Order
                {
                    Id = NewId(), Number = await NextAsync(AppDbContext.OrderNumberSeq, orderNumbers, ct), RequestId = request.Id, OfferId = chosen.Id,
                    PageId = page0.Id, CustomerId = customer.Id, Status = OrderStatus.InPurchase, SelectedAt = selectedAt, CreatedAt = selectedAt,
                };
                orders.Add(order);
                ledger.Add(new LedgerEntry { Id = NewId(), PageId = page0.Id, Type = LedgerType.FreeSelection, Amount = 0, OrderId = order.Id, Note = "اختيار مجاني", CreatedAt = selectedAt });

                var so = NewStoreOrder(order.Number, customer, page0, StoreOrderKind.Offer, chosen.Price, selectedAt);
                so.RequestId = request.Id;
                so.OfferOrderId = order.Id;
                so.Items.Add(new StoreOrderItem { Id = NewId(), OrderId = so.Id, ProductId = Guid.Empty, Name = request.Title, UnitPrice = chosen.Price, Quantity = 1, ImageKey = chosen.PhotoKey });
                so.Record(StoreOrderStatus.New, selectedAt);
                storeOrders.Add(so);
                Notify(ownerOf[page0.Id].Id, "العميل اختار عرضك", $"طلب {OrderNo(order.Number)} — {chosen.Price:N0} د.ع", "/seller/offers", selectedAt, plan != Plan.InPurchase);

                if (plan == Plan.InPurchase) continue;
                var t = selectedAt.AddMinutes(Int(15, 240));
                order.ContactedAt = t;
                order.Status = OrderStatus.AwaitingDelivery;
                so.Record(StoreOrderStatus.Accepted, Min(t, now.AddMinutes(-5)));
                if (plan == Plan.Awaiting)
                {
                    if (Chance(0.5)) SendOut(so, page0, Min(t.AddHours(Int(2, 12)), now.AddMinutes(-3)));
                    continue;
                }
                t = t.AddHours(Int(2, 12));
                SendOut(so, page0, t);
                t = t.AddHours(Int(2, 30));
                so.Record(StoreOrderStatus.Delivered, t);
                order.DeliveryAskedAt = t;
                order.Answer = DeliveryAnswer.Received;
                order.AnsweredAt = t.AddMinutes(Int(10, 600));
                order.Status = OrderStatus.Delivered;
                var agg = deals.GetValueOrDefault(page0.Id);
                agg.Deals++;
                Notify(customer.Id, $"تحديث طلبك {OrderNo(order.Number)}", "تم توصيل طلبك، قيّم الصفحة", $"/store/orders/{so.Id}", t, true);
                if (plan == Plan.Rated)
                {
                    var stars = rng.NextDouble() switch { < 0.6 => 5, < 0.88 => 4, < 0.96 => 3, _ => 2 };
                    var tags = stars >= 4 ? Sample(rules.RatingTags ?? [], Int(1, 2)) : Sample(rules.LowRatingTags ?? [], 1);
                    ratings.Add(new Rating
                    {
                        Id = NewId(), OrderId = order.Id, PageId = page0.Id, Stars = stars, Tags = tags.ToArray(),
                        Comment = Pick(PageReviews), CreatedAt = order.AnsweredAt.Value.AddMinutes(Int(1, 600)),
                    });
                    order.Status = OrderStatus.Rated;
                    agg.Sum += stars;
                    agg.Count++;
                }
                deals[page0.Id] = agg;
            }
            foreach (var p in pages)
                if (deals.TryGetValue(p.Id, out var d))
                {
                    p.Deals = d.Deals;
                    p.RatingCount = d.Count;
                    p.Rating = d.Count == 0 ? 0 : (double)d.Sum / d.Count;
                }
            foreach (var r in ratings.Where(r => r.CreatedAt > now)) r.CreatedAt = now.AddMinutes(-1);
        }

        private readonly Dictionary<string, string[]> schemaCache = [];

        private string[] SchemaList(string slug, string key)
        {
            var cacheKey = $"{slug}:{key}";
            if (schemaCache.TryGetValue(cacheKey, out var hit)) return hit;
            var schema = db.Categories.AsNoTracking().Where(c => c.Slug == slug).Select(c => c.FieldSchema).FirstOrDefault();
            string[] list = [];
            if (!string.IsNullOrWhiteSpace(schema))
            {
                using var doc = JsonDocument.Parse(schema);
                if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty(key, out var arr) && arr.ValueKind == JsonValueKind.Array)
                    list = arr.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToArray();
            }
            return schemaCache[cacheKey] = list;
        }

        // ---------- featured pages, ads, balances ----------

        private readonly HashSet<Guid> advertised = [];

        private void FeaturedAndAds()
        {
            var shops = ProductPages.Where(p => p.IsActive(now)).OrderByDescending(p => p.Deals).ThenByDescending(p => p.Rating).ThenBy(_ => rng.Next()).ToList();
            var order = 1;
            foreach (var p in shops.Take(o.FeaturedPages)) p.FeaturedOrder = order++;

            var banners = LiveSeedImages.ByTag["banner"];
            foreach (var page in Sample(shops, o.Banners))
            {
                var kit = kitOf[page.Id];
                var discount = Pick<int>([10, 15, 20, 25, 30]);
                var start = now.AddDays(-Int(1, 10)).Date.AddHours(Int(5, 12));
                ads.Add(new AdCampaign
                {
                    Id = NewId(), OwnerType = AdOwnerType.Page, PageId = page.Id, Slot = AdSlot.HomeBanner, Title = page.Name,
                    Headline = $"خصم {discount}% على {Headline(kit.Slug)}", DiscountPercent = discount,
                    GovernorateIds = Chance(0.5) ? [] : pageGovs.Where(g => g.PageId == page.Id).Select(g => g.GovernorateId).ToArray(),
                    CategoryIds = [cat[kit.Slug]], StartAt = start, EndAt = start.AddDays(Int(14, 30)), DailyPrice = rules.BannerDaily,
                    LastChargedOn = now.Date, Status = AdStatus.Active, ImageKey = Pick(banners), DeepLink = $"/pages/p/{page.Id}",
                    Impressions = Int(800, 9000), Clicks = Int(20, 400), CreatedAt = start.AddHours(-Int(2, 30)),
                });
                advertised.Add(page.Id);
            }
            foreach (var page in Sample(shops.Where(p => !advertised.Contains(p.Id)).ToList(), o.FeaturedAds))
            {
                var start = now.AddDays(-Int(1, 5)).Date.AddHours(Int(5, 12));
                ads.Add(new AdCampaign
                {
                    Id = NewId(), OwnerType = AdOwnerType.Page, PageId = page.Id, Slot = AdSlot.FeaturedPage, Title = page.Name,
                    Headline = page.Bio ?? page.Name, CategoryIds = [cat[kitOf[page.Id].Slug]], StartAt = start, EndAt = start.AddDays(7 * Int(1, 3)),
                    DailyPrice = rules.FeaturedWeekly / 7, LastChargedOn = now.Date, Status = AdStatus.Active, DeepLink = $"/pages/p/{page.Id}",
                    Impressions = Int(300, 4000), Clicks = Int(10, 200), CreatedAt = start.AddHours(-Int(2, 30)),
                });
                advertised.Add(page.Id);
            }
        }

        private static string Headline(string slug) => slug switch
        {
            "phones" => "الموبايلات والإكسسوارات", "shoes" => "الأحذية الرياضية", "clothes" => "الموديلات الجديدة", "perfumes" => "العطور",
            "watches" => "الساعات", "care" => "منتجات العناية", "home" => "الأجهزة المنزلية", "electronics" => "الألعاب واللابتوبات", _ => "بوكسات الهدايا",
        };

        /// <summary>Opening balance as one top-up ledger entry, minus what running ads already charged, so balance = sum of the ledger.</summary>
        private void Balances()
        {
            foreach (var page in pages.Where(p => p.Status == PageStatus.Verified))
            {
                var pageAds = ads.Where(a => a.PageId == page.Id).ToList();
                long topUp = pageAds.Count > 0 ? Pick<long>([100_000L, 150_000L, 200_000L]) : Chance(0.35) ? Pick<long>([10_000L, 25_000L, 50_000L]) : 0;
                if (topUp == 0) continue;
                var at = Between(page.CreatedAt.AddDays(1), pageAds.Count > 0 ? pageAds.Min(a => a.StartAt).AddHours(-1) : now.AddDays(-1));
                ledger.Add(new LedgerEntry { Id = NewId(), PageId = page.Id, Type = LedgerType.TopUp, Amount = topUp, Note = "شحن رصيد", CreatedAt = at });
                long balance = topUp;
                foreach (var ad in pageAds)
                {
                    var days = Math.Max(1, (int)Math.Ceiling((now - ad.StartAt).TotalDays));
                    var charge = Math.Min(balance, days * ad.DailyPrice);
                    balance -= charge;
                    ledger.Add(new LedgerEntry { Id = NewId(), PageId = page.Id, Type = LedgerType.AdCharge, Amount = -charge, RefId = ad.Id, Note = "إعلان", CreatedAt = now.AddHours(-Int(1, 12)) });
                }
                page.Balance = balance;
            }
        }

        private void SellerAccounts()
        {
            if (string.IsNullOrEmpty(o.SellerPassword)) return;
            var hash = owner.Passwords.Hash(o.SellerPassword); // one hash for the shared demo password keeps the run fast
            foreach (var page in ProductPages)
                sellers.Add(new SellerAccount
                {
                    Id = NewId(), PageId = page.Id, Email = $"{page.InstagramHandle}@{SellerEmailDomain}".ToLowerInvariant(),
                    PasswordHash = hash, CreatedAt = page.CreatedAt.AddDays(1),
                });
        }

        /// <summary>SaveChanges stamps updated_at = now; make it match the seeded history.</summary>
        private async Task AlignTimestampsAsync(CancellationToken ct)
        {
            string[] tables =
            [
                "pages", "page_follows", "couriers", "products", "product_favorites", "product_ratings", "requests", "offers", "ratings",
                "ledger_entries", "ad_campaigns", "notifications", "seller_accounts",
            ];
            foreach (var t in tables) await db.Database.ExecuteSqlRawAsync($"UPDATE {t} SET updated_at = created_at", ct);
            await db.Database.ExecuteSqlRawAsync("UPDATE users SET updated_at = created_at WHERE role <> 2", ct);
            await db.Database.ExecuteSqlRawAsync("UPDATE store_orders SET updated_at = status_changed_at", ct);
            await db.Database.ExecuteSqlRawAsync("UPDATE orders SET updated_at = COALESCE(answered_at, contacted_at, selected_at)", ct);
        }
    }
}
