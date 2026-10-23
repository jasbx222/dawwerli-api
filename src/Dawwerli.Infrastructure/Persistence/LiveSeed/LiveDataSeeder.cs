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
    IReadOnlyList<KeyValuePair<string, int>> Created, TimeSpan Elapsed, string? Showcase = null);

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
    public const string MarkerName = "live-seed-v2";
    /// <summary>pg_advisory_xact_lock key, so two app instances never seed at the same time.</summary>
    public const long LockKey = 0x64617777_6C697665; // "dawwlive"
    public const string SellerEmailDomain = "sellers.dawwerli.test";
    /// <summary>Page #1 is always the busiest page, so there is one known account to demo with.</summary>
    public const string ShowcasePhone = "07900020001";
    /// <summary>Wallet balance (IQD) every seeded page ends with; the ledger always sums to it.</summary>
    public const long PageBalance = 25_000;
    /// <summary>Must match the API's featured list size (PagesController.FeaturedTake).</summary>
    private const int FeaturedListSize = 20;
    public const string ShowcaseName = "مركز الرافدين للموبايلات";
    public const string ShowcaseHandle = "rafidain.mobile";
    public static string ShowcaseSellerEmail => $"{ShowcaseHandle}@{SellerEmailDomain}";

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

    /// <summary>
    /// True when the marker row holds the current <see cref="MarkerName"/>. A row left by an older version (e.g. live-seed-v1)
    /// does not count, so a server that already ran an older seed re-seeds once with Seed__Live=true.
    /// </summary>
    public async Task<bool> MarkerExistsAsync(CancellationToken ct = default)
    {
        var data = await db.Settings.AsNoTracking().Where(s => s.Id == MarkerId).Select(s => s.Data).FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(data)) return false;
        try
        {
            using var doc = JsonDocument.Parse(data);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("marker", out var m)
                && m.ValueKind == JsonValueKind.String && m.GetString() == MarkerName;
        }
        catch (JsonException) { return false; }
    }

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
            if (report.Showcase is not null) log.LogWarning("Live seed: showcase page: {Showcase}", report.Showcase);
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
            var run = new Run(this, o);
            var created = await run.ExecuteAsync(ct);
            db.Settings.Add(new SettingsRecord
            {
                Id = MarkerId, UpdatedAt = clock.UtcNow,
                Data = JsonSerializer.Serialize(new { marker = MarkerName, seededAt = clock.UtcNow, randomSeed = o.RandomSeed, counts = created.ToDictionary() }),
            });
            await db.SaveChangesAsync(ct);
            db.ChangeTracker.Clear();
            await tx.CommitAsync(ct);
            return new LiveSeedReport(false, "seeded", wiped, created, sw.Elapsed, run.ShowcaseSummary);
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
            $"plan: pages={o.Pages} (products on first {Math.Min(o.ProductPages, o.Pages)}, {o.MinProductsPerPage}-{o.MaxProductsPerPage} each), customers={o.Customers}, " +
            $"offers={o.Offers}, store_orders≈{o.StoreOrders}, ads={o.Ads}, featured_pages={o.FeaturedPages}, " +
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
        private int appPasswordOwners;
        private readonly Dictionary<Guid, List<Product>> productsOf = [];
        private readonly Dictionary<Guid, List<Courier>> couriersOf = [];
        private readonly Dictionary<Guid, ProductSeed> templateOf = [];
        private Page? showcase;
        public string? ShowcaseSummary { get; private set; }

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
            ShowcaseSummary = DescribeShowcase();

            return
            [
                new("customers", customers.Count), new("page_users", pages.Count), new("pages", pages.Count),
                new("pages_verified", pages.Count(p => p.Status == PageStatus.Verified)), new("page_categories", pageCats.Count),
                new("page_governorates", pageGovs.Count), new("couriers", couriers.Count), new("products", products.Count),
                new("page_follows", follows.Count), new("product_favorites", favorites.Count), new("store_orders", storeOrders.Count),
                new("store_order_items", storeOrders.Sum(s => s.Items.Count)), new("store_order_events", storeOrders.Sum(s => s.History.Count)),
                new("product_ratings", productRatings.Count), new("requests", requests.Count), new("request_dispatches", dispatches.Count),
                new("offers", offers.Count), new("orders", orders.Count), new("ratings", ratings.Count), new("ledger_entries", ledger.Count),
                new("ad_campaigns", ads.Count), new("ad_campaigns_running", ads.Count(a => a.Status == AdStatus.Active && a.StartAt <= now && a.EndAt > now)),
                new("sponsored_offers", offers.Count(x => x.Sponsored)), new("featured_pages", pages.Count(p => p.FeaturedOrder != null)),
                new("notifications", notes.Count), new("seller_accounts", sellers.Count),
            ];
        }

        private string? DescribeShowcase()
        {
            if (showcase is null) return null;
            var id = showcase.Id;
            var mine = offers.Where(x => x.PageId == id).ToList();
            var mineProducts = products.Where(x => x.PageId == id).Select(x => x.Id).ToHashSet();
            var so = storeOrders.Where(x => x.PageId == id).ToList();
            var email = sellers.Any(x => x.PageId == id) ? ShowcaseSellerEmail : "none (Seed__SellerPassword not set)";
            return $"{showcase.Name} (@{showcase.InstagramHandle}), app login phone {ShowcasePhone} (OTP{(appPasswordOwners > 0 ? $" or phone + Seed__SellerPassword; same for all {appPasswordOwners} page owners" : "")}), seller portal email {email}; " +
                $"products={products.Count(x => x.PageId == id)}, offers={mine.Count} (selected={mine.Count(x => x.Status == OfferStatus.Selected)}), " +
                $"store_orders={so.Count} ({string.Join(", ", so.GroupBy(x => x.Status).OrderBy(g => g.Key).Select(g => $"{g.Key}={g.Count()}"))}), " +
                $"followers={follows.Count(x => x.PageId == id)}, product_reviews={productRatings.Count(r => mineProducts.Contains(r.ProductId))}, " +
                $"deals={showcase.Deals}, rating={showcase.Rating:F1}, featured_order={showcase.FeaturedOrder}, " +
                $"ads={string.Join("+", ads.Where(a => a.PageId == id).Select(a => a.Slot))}, sponsored_offers={mine.Count(x => x.Sponsored)}, balance={showcase.Balance}";
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
            [
                ("phones", 12), ("clothes", 11), ("shoes", 8), ("perfumes", 6), ("watches", 4), ("care", 5), ("home", 4), ("electronics", 6), ("gifts", 3),
                ("furniture", 5), ("appliances", 5), ("gaming", 4), ("car-parts", 5), ("kids", 4), ("sports", 3), ("books", 3), ("beauty", 4),
                ("pharmacy", 3), ("food", 4), ("flowers", 3), ("pets", 2), ("tools", 3),
            ];

        private CategoryKit WeightedKit()
        {
            var x = rng.Next(KitWeights.Sum(k => k.Weight));
            foreach (var (slug, w) in KitWeights) { if (x < w) return Kits.First(k => k.Slug == slug); x -= w; }
            return Kits[0];
        }

        private void Pages()
        {
            var handles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ShowcaseHandle };
            var names = new HashSet<string> { ShowcaseName };
            // Almost every page is verified and receiving. A small realistic handful (about 2.4%: 1.2% waiting for review,
            // 0.6% suspended, 0.6% rejected) is not; their products stay in the database but the app hides them because
            // the storefront, search and feeds only list active pages. The showcase (page 1) is always verified.
            var odd = Sample(Enumerable.Range(2, Math.Max(0, o.Pages - 1)).ToList(), (int)Math.Round(o.Pages * 0.024));
            var statusOf = new Dictionary<int, PageStatus>();
            for (var k = 0; k < odd.Count; k++) statusOf[odd[k]] = (k % 4) switch { 0 or 1 => PageStatus.Pending, 2 => PageStatus.Suspended, _ => PageStatus.Rejected };
            for (var i = 1; i <= o.Pages; i++)
            {
                var kit = WeightedKit();
                var place = WeightedPlace();
                var word = Pick(ShopWords);
                var name = $"{Pick(kit.ShopPrefixes)} {word.Ar}";
                var isShowcase = i == 1 && o.ProductPages > 0;
                if (isShowcase)
                {
                    kit = Kits.First(k => k.Slug == "phones");
                    place = Places.First(p => p.Governorate == "بغداد");
                }
                if (isShowcase) name = ShowcaseName;
                else if (!names.Add(name)) { name = $"{name} - {place.Governorate}"; if (!names.Add(name)) { name = $"{name} {i}"; names.Add(name); } }
                var handle = isShowcase ? ShowcaseHandle : $"{word.Lat}.{kit.HandleTag}";
                if (!isShowcase && !handles.Add(handle))
                {
                    var alt = $"{word.Lat}.{kit.HandleTag}.iq";
                    var n = 2;
                    while (!handles.Add(alt)) alt = $"{word.Lat}{n++}.{kit.HandleTag}";
                    handle = alt;
                }

                var hasProducts = i <= o.ProductPages;
                var status = statusOf.GetValueOrDefault(i, PageStatus.Verified);
                var created = isShowcase ? DaysAgo(o.HistoryDays * 0.97)
                    : status == PageStatus.Pending ? Between(DaysAgo(6), DaysAgo(1))
                    : hasProducts ? Between(DaysAgo(o.HistoryDays), DaysAgo(o.HistoryDays * 0.35)) : Between(DaysAgo(o.HistoryDays), DaysAgo(1));
                var phone = $"0790002{i:D4}";
                var govId = gov[place.Governorate];
                var user = new User
                {
                    Id = NewId(), Role = UserRole.Page, PhoneHash = owner.Phones.Hash(phone), PhoneEncrypted = owner.Phones.Encrypt(phone),
                    Name = name, GovernorateId = govId, Area = Pick(place.Areas), CreatedAt = created.AddMinutes(-Int(5, 90)),
                };
                var page = new Page
                {
                    Id = NewId(), UserId = user.Id, Name = name, InstagramHandle = handle, Bio = Pick(kit.Bios), ImageKey = Image(Pick(kit.AvatarTags)),
                    Status = status, BioCode = $"DW{Int(100000, 999999)}", Receiving = status == PageStatus.Verified,
                    CreatedAt = created,
                    RejectReason = status == PageStatus.Rejected ? "رابط الانستغرام ما يطابق اسم الصفحة" : null,
                    SuspendedUntil = status == PageStatus.Suspended ? now.AddDays(Int(2, 10)) : null,
                };
                if (isShowcase)
                {
                    page.Bio = "موبايلات جديدة ومستعملة نظيفة مع الضمان، وأجهزة وإكسسوارات. توصيل لكل بغداد والمحافظات القريبة";
                    showcase = page;
                }
                users.Add(user);
                pages.Add(page);
                ownerOf[page.Id] = user;
                kitOf[page.Id] = kit;
                placeOf[page.Id] = place;

                pageCats.Add(new PageCategory { PageId = page.Id, CategoryId = cat[kit.Slug] });
                if (isShowcase) pageCats.Add(new PageCategory { PageId = page.Id, CategoryId = cat["electronics"] });
                else if (Chance(0.3))
                {
                    var extra = Pick(RelatedCategories[kit.Slug]);
                    pageCats.Add(new PageCategory { PageId = page.Id, CategoryId = cat[extra] });
                }
                pageGovs.Add(new PageGovernorate { PageId = page.Id, GovernorateId = govId });
                if ((isShowcase || Chance(0.25)) && neighbours.TryGetValue(govId, out var near) && near.Length > 0)
                    foreach (var n in Sample(near, isShowcase ? 2 : Int(1, 2)))
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
                for (var k = page == showcase ? 2 : Int(1, 2); k > 0; k--)
                    list.Add(new Courier
                    {
                        Id = NewId(), PageId = page.Id, Name = $"{Pick(MaleNames)} {Pick(FamilyNames)}", Phone = $"0790003{n++:D4}",
                        Active = true, CreatedAt = page.CreatedAt.AddDays(Int(1, 10)),
                    });
                couriers.AddRange(list);
                couriersOf[page.Id] = list;
            }
        }

        /// <summary>Extra spec lines appended to the templates until a page can list the wanted number of distinct items.</summary>
        private static readonly Dictionary<string, string[]> FallbackVariants = new()
        {
            ["phones"] = ["أسود", "أبيض", "أزرق", "ذهبي", "بنفسجي", "أخضر"], ["shoes"] = ["أسود", "أبيض", "رمادي", "كحلي", "بني", "بيج"],
            ["clothes"] = ["أسود", "كحلي", "أبيض", "رمادي", "بيج", "زيتي"], ["perfumes"] = ["تستر", "نسخة هدية", "50 مل", "100 مل", "200 مل", "طقم مع لوشن"],
            ["watches"] = ["فضي", "ذهبي", "أسود", "ذهبي وردي", "سير جلد", "سير ستيل"], ["care"] = ["حجم صغير", "حجم كبير", "طقم مزدوج", "للبشرة الدهنية", "للبشرة الجافة", "عرض 2+1"],
            ["home"] = ["لون أبيض", "لون رمادي", "لون بيج", "موديل 2025", "موديل 2026", "حجم عائلي"], ["electronics"] = ["أسود", "أبيض", "موديل 2025", "موديل 2026", "نسخة عالمية", "كفالة سنتين"],
            ["gifts"] = ["حجم وسط", "حجم كبير", "تغليف ذهبي", "تغليف أحمر", "مع كارت", "مع ورد"], ["furniture"] = ["لون جوزي", "لون أبيض", "لون رمادي", "لون بيج", "صناعة تركية", "صناعة محلية"],
            ["appliances"] = ["أبيض", "فضي", "أسود", "موديل 2025", "موديل 2026", "كفالة 3 سنوات"], ["gaming"] = ["أسود", "أبيض", "نسخة أوروبية", "نسخة أمريكية", "مع لعبة", "مع يد إضافية"],
            ["car-parts"] = ["كوري", "ياباني", "ألماني", "صيني درجة أولى", "أصلي وكالة", "تجاري"], ["kids"] = ["لون وردي", "لون أزرق", "لون أحمر", "لون أصفر", "عمر 1-3", "عمر 3-6"],
            ["sports"] = ["أسود", "أزرق", "أحمر", "رمادي", "حجم كبير", "حجم صغير"], ["books"] = ["طبعة 2025", "طبعة 2026", "غلاف مقوى", "غلاف ورقي", "حجم جيب", "طبعة ملونة"],
            ["beauty"] = ["درجة 01", "درجة 02", "درجة 03", "درجة 04", "حجم سفر", "طقم هدية"], ["pharmacy"] = ["عبوة 30", "عبوة 60", "عبوة 90", "حجم عائلي", "للأطفال", "للكبار"],
            ["food"] = ["نص كيلو", "كيلو", "2 كيلو", "علبة هدية", "صينية كبيرة", "صينية وسط"], ["flowers"] = ["أحمر", "أبيض", "وردي", "أصفر", "مشكل", "بنفسجي"],
            ["pets"] = ["حجم صغير", "حجم وسط", "حجم كبير", "للصغار", "للبالغين", "عرض قطعتين"], ["tools"] = ["بوش", "ماكيتا", "توتال", "انكو", "ستانلي", "ديوالت"],
        };

        private static readonly string[] GenericVariants = ["موديل 2025", "موديل 2026", "إصدار خاص", "حجم كبير", "حجم صغير", "نسخة اقتصادية"];

        private void Products()
        {
            foreach (var page in ProductPages)
            {
                var kit = kitOf[page.Id];
                var want = page == showcase ? o.MaxProductsPerPage : Int(o.MinProductsPerPage, o.MaxProductsPerPage);
                if (want == 0) { productsOf[page.Id] = []; continue; }
                // Wall-placeable templates are always listed with every size, so the variant switcher has the full group.
                var wall = kit.Products.Where(t => t.Wall is not null)
                    .SelectMany(t => t.Variants!.Select((v, i) => (Seed: t, Name: $"{t.Name} {v}", Used: false, Variant: i))).Take(want).ToList();
                var candidates = new List<(ProductSeed Seed, string Name, bool Used, int Variant)>();
                foreach (var t in kit.Products.Where(t => t.Wall is null))
                {
                    if (t.Variants is { Length: > 0 }) candidates.AddRange(t.Variants.Select(v => (t, $"{t.Name} {v}", false, -1)));
                    else candidates.Add((t, t.Name, false, -1));
                    if (t.CanBeUsed) candidates.Add((t, $"{t.Name} مستعمل", true, -1));
                }
                var rest = want - wall.Count;
                foreach (var v in FallbackVariants.GetValueOrDefault(kit.Slug, GenericVariants).Concat(GenericVariants).Distinct())
                {
                    if (candidates.Count >= rest) break;
                    candidates.AddRange(kit.Products.Where(t => t.Wall is null).Select(t => (t, $"{t.Name} - {v}", false, -1)));
                }

                var list = new List<Product>();
                var groupImages = new Dictionary<string, string[]>();
                foreach (var (seed, name, used, variant) in wall.Concat(Sample(candidates, rest)))
                {
                    var price = used ? RoundPrice(Price(seed.MinPrice, seed.MaxPrice) * (0.6 + rng.NextDouble() * 0.15)) : Price(seed.MinPrice, seed.MaxPrice);
                    var condition = used ? "الحالة: مستعمل نظيف، مفحوص ومضمون." : "الحالة: جديد بالكارتون.";
                    var p = new Product
                    {
                        Id = NewId(), PageId = page.Id, Name = name, Description = $"{seed.Description}\n{condition}\nالسعر بالدينار العراقي، التوصيل متوفر.",
                        Price = price, ImageKeys = Images(seed.Tag, Int(1, 3)), CategoryId = cat[kit.Slug],
                        Stock = page != showcase && Chance(0.08) ? 0 : Int(1, 25), Active = page == showcase || Chance(0.95),
                        CreatedAt = Between(page.CreatedAt.AddDays(1), DaysAgo(1)),
                    };
                    if (seed.Wall is { } w && variant >= 0) ApplyWall(p, seed, w, variant, groupImages);
                    list.Add(p);
                    templateOf[p.Id] = seed;
                }
                products.AddRange(list);
                productsOf[page.Id] = list;
            }
        }

        /// <summary>Size, kind and group for one variant; prices climb with the size and the group shares one set of photos.</summary>
        private void ApplyWall(Product p, ProductSeed seed, WallSeed w, int variant, Dictionary<string, string[]> groupImages)
        {
            var (width, height, depth) = w.Sizes[variant];
            p.WidthCm = width;
            p.HeightCm = height;
            p.DepthCm = depth;
            p.WallKind = w.Kind;
            p.VariantGroup = w.Group;
            p.VariantLabel = seed.Variants![variant];
            p.ImageKeys = groupImages.TryGetValue(w.Group, out var shared) ? shared : groupImages[w.Group] = p.ImageKeys;
            var step = w.Sizes.Length > 1 ? (double)variant / (w.Sizes.Length - 1) : 0;
            p.Price = RoundPrice(seed.MinPrice + (seed.MaxPrice - seed.MinPrice) * step * (0.95 + rng.NextDouble() * 0.05));
        }

        private List<Page> LivePages => pages.Where(p => p.IsActive(now)).ToList();

        private void FollowsAndFavorites()
        {
            var live = LivePages;
            var withProducts = live.Where(p => productsOf.TryGetValue(p.Id, out var l) && l.Count > 0).ToList();
            var liveIds = live.Select(p => p.Id).ToHashSet();
            var visible = products.Where(p => p.Visible && liveIds.Contains(p.PageId)).ToList();
            var pairs = new HashSet<(Guid, Guid)>();
            foreach (var c in customers)
            {
                var chosen = new HashSet<Guid>();
                for (var k = Int(2, 8); k > 0; k--)
                {
                    var p = Chance(0.7) && withProducts.Count > 0 ? Pick(withProducts) : Pick(live);
                    if (!chosen.Add(p.Id)) continue;
                    pairs.Add((c.Id, p.Id));
                    follows.Add(new PageFollow { Id = NewId(), UserId = c.Id, PageId = p.Id, CreatedAt = Between(Max(c.CreatedAt, p.CreatedAt), now.AddHours(-1)) });
                }
                if (showcase is not null && live.Contains(showcase) && !chosen.Contains(showcase.Id) && Chance(0.45) && pairs.Add((c.Id, showcase.Id)))
                    follows.Add(new PageFollow { Id = NewId(), UserId = c.Id, PageId = showcase.Id, CreatedAt = Between(Max(c.CreatedAt, showcase.CreatedAt), now.AddHours(-1)) });
                var favs = new HashSet<Guid>();
                for (var k = Int(0, 6); k > 0 && visible.Count > 0; k--)
                {
                    var pr = Pick(visible);
                    if (!favs.Add(pr.Id)) continue;
                    favorites.Add(new ProductFavorite { Id = NewId(), UserId = c.Id, ProductId = pr.Id, CreatedAt = Between(Max(c.CreatedAt, pr.CreatedAt), now.AddHours(-1)) });
                }
            }
            // Every page that customers could ever see (verified, or suspended after trading) gets at least one follower.
            // Pending and rejected pages were never public, so nobody follows them.
            var followed = follows.Select(f => f.PageId).ToHashSet();
            foreach (var p in pages.Where(p => p.Status is PageStatus.Verified or PageStatus.Suspended && !followed.Contains(p.Id)))
                foreach (var c in Sample(customers, Int(1, 4)))
                    if (pairs.Add((c.Id, p.Id)))
                        follows.Add(new PageFollow { Id = NewId(), UserId = c.Id, PageId = p.Id, CreatedAt = Between(Max(c.CreatedAt, p.CreatedAt), now.AddHours(-1)) });
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

            // The showcase page gets a fixed share covering every status.
            StoreOrderStatus[] showcaseMix = showcase is not null && shops.Contains(showcase)
                ?
                [
                    StoreOrderStatus.Delivered, StoreOrderStatus.New, StoreOrderStatus.OutForDelivery, StoreOrderStatus.Delivered,
                    StoreOrderStatus.Accepted, StoreOrderStatus.Delivered, StoreOrderStatus.Cancelled, StoreOrderStatus.Delivered,
                    StoreOrderStatus.OutForDelivery, StoreOrderStatus.New, StoreOrderStatus.Delivered, StoreOrderStatus.Rejected,
                    StoreOrderStatus.Accepted, StoreOrderStatus.Delivered,
                ]
                : [];
            for (var i = 0; i < o.StoreOrders; i++)
            {
                var forShowcase = i < showcaseMix.Length;
                var status = forShowcase ? showcaseMix[i] : StoreStatus();
                var customer = Pick(customers);
                // Prefer shops that deliver to the customer's governorate.
                var local = shops.Where(p => pageGovs.Any(g => g.PageId == p.Id && g.GovernorateId == customer.GovernorateId)).ToList();
                var page = forShowcase ? showcase! : local.Count > 0 && Chance(0.8) ? Pick(local) : Pick(shops);
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
                    if (!Chance(forShowcase ? 0.9 : 0.6) || !reviewed.Add((customer.Id, it.ProductId))) continue;
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
            // The showcase page bids on the first ten requests (one of every plan) and wins five of them.
            var showcaseLive = showcase is not null && live.Contains(showcase);
            var showcaseGovs = showcaseLive ? pageGovs.Where(g => g.PageId == showcase!.Id).Select(g => (int?)g.GovernorateId).ToHashSet() : [];
            var nearShowcase = customers.Where(c => showcaseGovs.Contains(c.GovernorateId)).ToList();
            for (var index = 0; remaining > 0; index++)
            {
                var plan = NextPlan(index);
                var showcaseTurn = showcaseLive && index < 10;
                var customer = showcaseTurn && nearShowcase.Count > 0 ? Pick(nearShowcase) : Pick(customers);
                var kit = showcaseTurn ? Kits.First(k => k.Slug == (index % 3 == 2 ? "electronics" : "phones")) : Kits[rng.Next(Kits.Length)];
                var catId = cat[kit.Slug];
                var govId = customer.GovernorateId!.Value;
                var matching = live.Where(p => pageCats.Any(c => c.PageId == p.Id && c.CategoryId == catId)
                    && pageGovs.Any(g => g.PageId == p.Id && g.GovernorateId == govId)).ToList();
                if (matching.Count == 0) matching = live.Where(p => pageCats.Any(c => c.PageId == p.Id && c.CategoryId == catId)).ToList();
                if (showcaseTurn && !matching.Contains(showcase!)) matching.Add(showcase!);
                if (matching.Count == 0) continue;

                var want = Math.Min(remaining, plan == Plan.Expired ? Int(1, 2) : Int(1, 4));
                var bidders = Sample(showcaseTurn ? matching.Where(p => p != showcase).ToList() : matching, showcaseTurn ? want - 1 : want);
                if (showcaseTurn) bidders.Insert(rng.Next(bidders.Count + 1), showcase!);
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
                if (showcaseTurn && index is 1 or 2 or 3 or 4 or 9) chosen = made.First(x => x.PageId == showcase!.Id);
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

        private sealed record AdPlan(AdSlot Slot, AdOwnerType Owner, AdStatus Status, bool Scheduled = false);

        // 50 campaigns: 20 home banners (14 page, 3 brand, 3 platform), 16 featured pages, 14 sponsored offers.
        // Mostly running; each slot also has scheduled, pending review, ended and paused or rejected ones.
        // The lists are interleaved, so a smaller Seed__Ads still covers every slot, owner type and status early on.
        private static readonly AdPlan[] BannerPlans =
        [
            new(AdSlot.HomeBanner, AdOwnerType.Page, AdStatus.Active), new(AdSlot.HomeBanner, AdOwnerType.Brand, AdStatus.Active),
            new(AdSlot.HomeBanner, AdOwnerType.Platform, AdStatus.Active), new(AdSlot.HomeBanner, AdOwnerType.Page, AdStatus.Pending),
            new(AdSlot.HomeBanner, AdOwnerType.Page, AdStatus.Active), new(AdSlot.HomeBanner, AdOwnerType.Page, AdStatus.Ended),
            new(AdSlot.HomeBanner, AdOwnerType.Brand, AdStatus.Active), new(AdSlot.HomeBanner, AdOwnerType.Page, AdStatus.Active, true),
            new(AdSlot.HomeBanner, AdOwnerType.Page, AdStatus.PausedBalance), new(AdSlot.HomeBanner, AdOwnerType.Platform, AdStatus.Ended),
            new(AdSlot.HomeBanner, AdOwnerType.Page, AdStatus.Active), new(AdSlot.HomeBanner, AdOwnerType.Brand, AdStatus.Active),
            new(AdSlot.HomeBanner, AdOwnerType.Page, AdStatus.Active), new(AdSlot.HomeBanner, AdOwnerType.Platform, AdStatus.Active),
            new(AdSlot.HomeBanner, AdOwnerType.Page, AdStatus.Active), new(AdSlot.HomeBanner, AdOwnerType.Page, AdStatus.Active),
            new(AdSlot.HomeBanner, AdOwnerType.Page, AdStatus.Active), new(AdSlot.HomeBanner, AdOwnerType.Page, AdStatus.Active),
            new(AdSlot.HomeBanner, AdOwnerType.Page, AdStatus.Active), new(AdSlot.HomeBanner, AdOwnerType.Page, AdStatus.Active),
        ];

        private static readonly AdPlan[] FeaturedPlans =
        [
            new(AdSlot.FeaturedPage, AdOwnerType.Page, AdStatus.Active), new(AdSlot.FeaturedPage, AdOwnerType.Page, AdStatus.Pending),
            new(AdSlot.FeaturedPage, AdOwnerType.Page, AdStatus.Active), new(AdSlot.FeaturedPage, AdOwnerType.Page, AdStatus.Ended),
            new(AdSlot.FeaturedPage, AdOwnerType.Page, AdStatus.Active, true), new(AdSlot.FeaturedPage, AdOwnerType.Page, AdStatus.Rejected),
            new(AdSlot.FeaturedPage, AdOwnerType.Page, AdStatus.Active), new(AdSlot.FeaturedPage, AdOwnerType.Page, AdStatus.PausedSuspended),
            .. Enumerable.Repeat(new AdPlan(AdSlot.FeaturedPage, AdOwnerType.Page, AdStatus.Active), 8),
        ];

        private static readonly AdPlan[] SponsoredPlans =
        [
            new(AdSlot.SponsoredOffer, AdOwnerType.Page, AdStatus.Active), new(AdSlot.SponsoredOffer, AdOwnerType.Page, AdStatus.PausedBalance),
            new(AdSlot.SponsoredOffer, AdOwnerType.Page, AdStatus.Active), new(AdSlot.SponsoredOffer, AdOwnerType.Page, AdStatus.Pending),
            new(AdSlot.SponsoredOffer, AdOwnerType.Page, AdStatus.Active, true), new(AdSlot.SponsoredOffer, AdOwnerType.Page, AdStatus.Ended),
            new(AdSlot.SponsoredOffer, AdOwnerType.Page, AdStatus.Active), new(AdSlot.SponsoredOffer, AdOwnerType.Page, AdStatus.Rejected),
            .. Enumerable.Repeat(new AdPlan(AdSlot.SponsoredOffer, AdOwnerType.Page, AdStatus.Active), 6),
        ];

        internal static IReadOnlyList<(AdSlot Slot, AdOwnerType Owner, AdStatus Status, bool Scheduled)> AdMix(int count)
        {
            var mixed = new List<AdPlan>();
            for (var i = 0; mixed.Count < BannerPlans.Length + FeaturedPlans.Length + SponsoredPlans.Length; i++)
                foreach (var list in new[] { BannerPlans, FeaturedPlans, SponsoredPlans })
                    if (i < list.Length) mixed.Add(list[i]);
            return Enumerable.Range(0, count).Select(i => mixed[i % mixed.Count]).Select(p => (p.Slot, p.Owner, p.Status, p.Scheduled)).ToList();
        }

        /// <summary>Fictional distributors that book national home banners (paid offline, no page wallet).</summary>
        private static readonly (string Name, string Slug, string Headline, string Tag)[] Brands =
        [
            ("مجموعة دجلة للإلكترونيات", "electronics", "لابتوبات وسماعات بكفالة الوكيل — توصيل لكل المحافظات", "laptop"),
            ("شركة النخيل للأجهزة المنزلية", "appliances", "سبلتات انفيرتر توفر بالأمبيرات — تقسيط بدون فوائد", "fridge"),
            ("بيت العطور الشرقية", "perfumes", "تشكيلة عطور الشتاء وصلت — هدية مع كل طلب", "perfume"),
            ("الرافدين للإطارات والبطاريات", "car-parts", "بدّل تايرات سيارتك قبل الشتاء — فحص مجاني", "tire"),
            ("عالم الألعاب", "gaming", "أجهزة وألعاب أصلية بأسعار الجملة", "console"),
        ];

        private static readonly (string Title, string Headline)[] PlatformBanners =
        [
            ("دوّرلي", "اكتب شنو تريد وخلي المحلات تتنافس على أحسن سعر"),
            ("دوّرلي", "تسوّق من متاجر موثّقة وادفع عند الاستلام"),
            ("دوّرلي", "عروض الجمعة: آلاف المنتجات من محلات مدينتك"),
        ];

        private static readonly string[] RejectNotes =
        [
            "الصورة فيها نص كثير وما واضح المنتج، يرجى رفع صورة أوضح",
            "العنوان ما يطابق محتوى الصفحة",
            "نسبة الخصم بالإعلان ما تطابق الأسعار المعروضة",
        ];

        private void FeaturedAndAds()
        {
            var shops = ProductPages.Where(p => p.IsActive(now)).OrderByDescending(p => p.Deals).ThenByDescending(p => p.Rating).ThenBy(_ => rng.Next()).ToList();
            var hasShowcase = showcase is not null && shops.Remove(showcase);
            var order = 1;
            if (hasShowcase && o.FeaturedPages > 0) showcase!.FeaturedOrder = order++;
            foreach (var p in shops.Take(o.FeaturedPages - (order - 1))) p.FeaturedOrder = order++;
            if (o.Ads == 0) return;

            // Advertisers: featured pages come from the best ranked shops (the featured list only shows the top pages of a
            // category and governorate), sponsored offers from pages that are bidding on requests, banners from anyone.
            var bidders = offers.GroupBy(x => x.PageId).OrderByDescending(g => g.Count(x => x.Status == OfferStatus.Active)).ThenByDescending(g => g.Count())
                .Select(g => g.Key).ToList();
            var bidderSet = bidders.ToHashSet();
            var shopById = shops.ToDictionary(p => p.Id);
            // The featured list shows the top FeaturedListSize active pages of a category + governorate by deals and rating, and
            // only flags paid ones inside it. Featured advertisers are pages that make that cut: ranked pages, or pages in a
            // category + governorate with few competitors.
            var activeIds = shops.Select(p => p.Id).Append(showcase?.Id ?? Guid.Empty).ToHashSet();
            var govsOf = pageGovs.Where(g => activeIds.Contains(g.PageId)).ToLookup(g => g.PageId, g => g.GovernorateId);
            var crowd = pageCats.Where(c => activeIds.Contains(c.PageId)).SelectMany(c => govsOf[c.PageId].Select(g => (c.CategoryId, g)))
                .GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
            int Crowd(Page p) => govsOf[p.Id].Select(g => crowd.GetValueOrDefault((cat[kitOf[p.Id].Slug], g))).DefaultIfEmpty(int.MaxValue).Min();
            var pools = new Dictionary<AdSlot, Queue<Page>>
            {
                [AdSlot.FeaturedPage] = new(shops.Where(p => p.Deals > 0 || p.Rating > 0 || Crowd(p) <= FeaturedListSize)),
                [AdSlot.SponsoredOffer] = new(bidders.Where(shopById.ContainsKey).Select(id => shopById[id]).Concat(Sample(shops.Where(p => !bidderSet.Contains(p.Id)).ToList(), shops.Count))),
                [AdSlot.HomeBanner] = new(Sample(shops, shops.Count)),
            };
            var suspended = new Queue<Page>(pages.Where(p => p.Status == PageStatus.Suspended && productsOf.ContainsKey(p.Id)));
            var usedBySlot = new Dictionary<AdSlot, HashSet<Guid>> { [AdSlot.HomeBanner] = [], [AdSlot.FeaturedPage] = [], [AdSlot.SponsoredOffer] = [] };
            var banners = LiveSeedImages.ByTag["banner"];
            int brand = 0, platform = 0, reject = 0;

            Page? NextPage(AdSlot slot, AdStatus status)
            {
                if (hasShowcase && status == AdStatus.Active && usedBySlot[slot].Add(showcase!.Id)) return showcase;
                if (status == AdStatus.PausedSuspended && suspended.Count > 0) return suspended.Dequeue();
                var pool = pools[slot];
                while (pool.Count > 0)
                {
                    var p = pool.Dequeue();
                    if (usedBySlot[slot].Add(p.Id)) return p;
                }
                var rest = shops.Where(p => !usedBySlot[slot].Contains(p.Id)).ToList();
                if (rest.Count == 0) rest = shops;
                if (rest.Count == 0) return null;
                var pick = Pick(rest);
                usedBySlot[slot].Add(pick.Id);
                return pick;
            }

            foreach (var (slot, ownerType, status, scheduled) in AdMix(o.Ads))
            {
                var page = ownerType == AdOwnerType.Page ? NextPage(slot, status) : null;
                if (ownerType == AdOwnerType.Page && page is null) continue;

                // Dates: running ads started in the last days, scheduled ones start soon, ended ones ran months ago.
                var days = slot switch { AdSlot.FeaturedPage => 7 * Int(2, 4), AdSlot.SponsoredOffer => Int(14, 30), _ => Int(14, 30) };
                DateTime start, created;
                switch (status)
                {
                    case AdStatus.Active when scheduled:
                    case AdStatus.Pending:
                        created = now.AddHours(-Int(2, 40));
                        start = now.Date.AddDays(Int(1, 5)).AddHours(Int(5, 9));
                        break;
                    case AdStatus.Ended:
                        start = now.AddDays(-Int(35, 120)).Date.AddHours(Int(5, 12));
                        days = Math.Min(days, Int(7, 21));
                        created = start.AddHours(-Int(2, 30));
                        break;
                    case AdStatus.Rejected:
                        created = now.AddDays(-Int(2, 12)).AddHours(-Int(0, 12));
                        start = created.Date.AddDays(1).AddHours(Int(5, 9));
                        break;
                    case AdStatus.PausedBalance:
                        // Booked for longer than the 25,000 IQD balance covers, so the daily charge job paused it.
                        days = Math.Max(days, slot == AdSlot.HomeBanner ? 10 : 20);
                        start = now.AddDays(-Int(1, 3)).Date.AddHours(Int(5, 9));
                        created = start.AddHours(-Int(2, 20));
                        break;
                    default:
                        start = now.AddDays(-Int(1, slot == AdSlot.FeaturedPage ? 6 : 10)).Date.AddHours(Int(5, 12));
                        created = start.AddHours(-Int(2, 30));
                        break;
                }
                if (page is not null && created < page.CreatedAt.AddDays(2)) created = page.CreatedAt.AddDays(2);
                if (start < created) start = created.AddHours(1);
                var end = start.AddDays(days);
                if (status == AdStatus.Active && !scheduled && end <= now.AddDays(1)) end = now.AddDays(Int(3, 10));

                var dailyPrice = AdsDailyPrice(slot);
                var ad = new AdCampaign
                {
                    Id = NewId(), OwnerType = ownerType, PageId = page?.Id, Slot = slot, StartAt = start, EndAt = end, DailyPrice = dailyPrice,
                    Status = status, CreatedAt = created,
                };
                switch (ownerType)
                {
                    case AdOwnerType.Brand:
                    {
                        var b = Brands[brand++ % Brands.Length];
                        var dealer = shops.Where(p => kitOf[p.Id].Slug == b.Slug).ToList();
                        ad.Title = b.Name;
                        ad.Headline = b.Headline;
                        ad.DiscountPercent = Pick<int>([0, 10, 15, 20]);
                        ad.CategoryIds = cat.TryGetValue(b.Slug, out var bc) ? [bc] : [];
                        ad.ImageKey = LiveSeedImages.ByTag.TryGetValue(b.Tag, out var bi) ? Pick(bi) : Pick(banners);
                        ad.DeepLink = dealer.Count > 0 ? $"/pages/p/{Pick(dealer).Id}" : null;
                        break;
                    }
                    case AdOwnerType.Platform:
                    {
                        var (title, headline) = PlatformBanners[platform++ % PlatformBanners.Length];
                        ad.Title = title;
                        ad.Headline = status == AdStatus.Ended ? "عروض الصيف على دوّرلي — خصومات من محلات العراق" : headline;
                        ad.ImageKey = Pick(banners);
                        break;
                    }
                    default:
                    {
                        var kit = kitOf[page!.Id];
                        var govs = pageGovs.Where(g => g.PageId == page.Id).Select(g => g.GovernorateId).ToArray();
                        ad.Title = page.Name;
                        ad.DeepLink = $"/pages/p/{page.Id}";
                        ad.CategoryIds = [cat[kit.Slug]];
                        ad.GovernorateIds = slot == AdSlot.HomeBanner && Chance(0.5) ? [] : govs;
                        var pageProducts = productsOf.GetValueOrDefault(page.Id) ?? [];
                        if (slot == AdSlot.HomeBanner)
                        {
                            ad.DiscountPercent = Pick<int>([10, 15, 20, 25, 30]);
                            ad.Headline = $"خصم {ad.DiscountPercent}% على {Headline(kit.Slug)}";
                            ad.ImageKey = Pick(banners);
                        }
                        else if (slot == AdSlot.FeaturedPage)
                        {
                            ad.Headline = page.Bio ?? $"أفضل أسعار {Headline(kit.Slug)}";
                            ad.ImageKey = page.ImageKey;
                        }
                        else
                        {
                            // Shown to customers as the timed home popup: a short pitch and always an image (no image = never shown).
                            ad.Headline = Pick<string>(["سعر منافس وتوصيل سريع لكل المحافظات — شوف عرضنا", "أسعار جملة للمفرد، جرّب عرضنا اليوم",
                                $"{Headline(kit.Slug)} أصلية بكفالة — عرض لفترة محدودة"]);
                            ad.ImageKey = (pageProducts.Count > 0 ? pageProducts[0].ImageKeys.FirstOrDefault() : null) ?? page.ImageKey ?? Pick(banners);
                        }
                        if (status == AdStatus.Rejected) ad.Note = RejectNotes[reject++ % RejectNotes.Length];
                        if (status == AdStatus.PausedSuspended) ad.Note = "متوقف مؤقتاً بسبب تعليق الصفحة";
                        advertised.Add(page.Id);
                        break;
                    }
                }

                // Delivery stats: what the ad got while it was running.
                var ran = status switch
                {
                    AdStatus.Active when !scheduled => Math.Max(0.3, (now - start).TotalDays),
                    AdStatus.Ended => (end - start).TotalDays,
                    AdStatus.PausedSuspended or AdStatus.PausedBalance => Math.Max(0.2, Math.Min((now - start).TotalDays, 2)),
                    _ => 0,
                };
                var perDay = slot switch { AdSlot.HomeBanner => Int(400, 1500), AdSlot.FeaturedPage => Int(80, 350), _ => Int(20, 90) };
                ad.Impressions = (long)(ran * perDay);
                ad.Clicks = (long)(ad.Impressions * (rng.NextDouble() * 0.04 + 0.015));

                // Page ads are prepaid like AdsService.CreateAsync: the whole booking is charged up front.
                // Rejected ads got the money back; a page suspended mid-campaign got the unused days back; PausedBalance never paid.
                if (page is not null && dailyPrice > 0 && status != AdStatus.PausedBalance)
                {
                    var bookedDays = (int)Math.Round((end - start).TotalDays);
                    var total = dailyPrice * bookedDays;
                    ad.LastChargedOn = end.Date;
                    ledger.Add(new LedgerEntry { Id = NewId(), PageId = page.Id, Type = LedgerType.AdCharge, Amount = -total, RefId = ad.Id, Note = $"إعلان: {ad.Title}", CreatedAt = created });
                    long refund = status switch
                    {
                        AdStatus.Rejected => total,
                        AdStatus.PausedSuspended => Math.Min(total, Math.Max(0, (int)Math.Floor((end - now).TotalDays)) * dailyPrice),
                        _ => 0,
                    };
                    if (refund > 0)
                        ledger.Add(new LedgerEntry { Id = NewId(), PageId = page.Id, Type = LedgerType.AdRefund, Amount = refund, RefId = ad.Id, Note = $"استرجاع مبلغ إعلان: {ad.Title}",
                            CreatedAt = status == AdStatus.Rejected ? created.AddHours(Int(2, 20)) : Between(start, now.AddHours(-1)) });
                }
                else if (page is null) ad.LastChargedOn = end.Date;
                ads.Add(ad);
            }

            MarkSponsoredOffers();
        }

        private long AdsDailyPrice(AdSlot slot) => slot switch
        {
            AdSlot.HomeBanner => rules.BannerDaily,
            AdSlot.FeaturedPage => rules.FeaturedWeekly / 7,
            _ => rules.SponsoredOffer,
        };

        /// <summary>
        /// Same rule as OfferService: the first offer on a request from a page whose sponsored-offer ad was running at that
        /// moment is marked sponsored and pinned on the request (it is listed first).
        /// </summary>
        private void MarkSponsoredOffers()
        {
            var sponsoredAds = ads.Where(a => a.Slot == AdSlot.SponsoredOffer && a.PageId != null
                && a.Status is AdStatus.Active or AdStatus.Ended or AdStatus.PausedSuspended).ToList();
            if (sponsoredAds.Count == 0) return;
            var byPage = sponsoredAds.GroupBy(a => a.PageId!.Value).ToDictionary(g => g.Key, g => g.ToList());
            var requestById = requests.ToDictionary(r => r.Id);
            foreach (var group in offers.GroupBy(x => x.RequestId))
            {
                var r = requestById[group.Key];
                if (r.SponsoredOfferId != null) continue;
                foreach (var offer in group.OrderBy(x => x.CreatedAt))
                {
                    if (!byPage.TryGetValue(offer.PageId, out var list)) continue;
                    var ad = list.FirstOrDefault(a => a.StartAt <= offer.CreatedAt && offer.CreatedAt < a.EndAt
                        && (a.Status != AdStatus.PausedSuspended || offer.CreatedAt < now.AddDays(-2)));
                    if (ad is null) continue;
                    offer.Sponsored = true;
                    r.SponsoredOfferId = offer.Id;
                    if (offer.Status == OfferStatus.Selected) ad.Selections++;
                    break;
                }
            }
        }

        private static string Headline(string slug) => slug switch
        {
            "phones" => "الموبايلات والإكسسوارات", "shoes" => "الأحذية الرياضية", "clothes" => "الموديلات الجديدة", "perfumes" => "العطور",
            "watches" => "الساعات", "care" => "منتجات العناية", "home" => "مستلزمات البيت", "electronics" => "الألعاب واللابتوبات",
            "furniture" => "الأثاث وغرف النوم", "appliances" => "الأجهزة المنزلية", "gaming" => "أجهزة وألعاب الفيديو", "car-parts" => "قطع غيار السيارات",
            "kids" => "ألعاب ومستلزمات الأطفال", "sports" => "المعدات الرياضية", "books" => "الكتب والقرطاسية", "beauty" => "المكياج والتجميل",
            "pharmacy" => "المستلزمات الصحية", "food" => "الحلويات والمأكولات", "flowers" => "الورد والنباتات", "pets" => "مستلزمات الحيوانات الأليفة",
            "tools" => "العدد والأدوات", _ => "بوكسات الهدايا",
        };

        /// <summary>Every page ends at <see cref="PageBalance"/>: one opening top-up covers whatever the ledger already moved, so balance = sum of the ledger.</summary>
        private void Balances()
        {
            var moved = ledger.GroupBy(e => e.PageId)
                .ToDictionary(g => g.Key, g => (Sum: g.Sum(e => e.Amount), First: g.Min(e => e.CreatedAt)));
            foreach (var page in pages)
            {
                var (sum, first) = moved.TryGetValue(page.Id, out var m) ? m : (Sum: 0L, First: now);
                var topUp = PageBalance - sum;
                var at = first.AddHours(-Int(2, 48));
                var floor = page.CreatedAt.AddHours(1);
                if (at < floor) at = floor;
                if (at > now.AddHours(-1)) at = Between(page.CreatedAt, now.AddHours(-1));
                if (topUp != 0)
                    ledger.Add(new LedgerEntry { Id = NewId(), PageId = page.Id, Type = LedgerType.TopUp, Amount = topUp, Note = "شحن رصيد", CreatedAt = at });
                page.Balance = PageBalance;
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
            // Every page owner can also sign in to the app with phone + password (POST /api/v1/auth/login):
            // OTP to the fictional numbers never arrives. Same precomputed hash, no extra hashing cost.
            foreach (var user in ownerOf.Values) user.PasswordHash = hash;
            appPasswordOwners = ownerOf.Count;
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
