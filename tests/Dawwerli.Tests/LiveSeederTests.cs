using System.Net.Http.Json;
using System.Net;
using Dawwerli.Domain.Entities;
using Dawwerli.Infrastructure.Persistence;
using Dawwerli.Infrastructure.Persistence.LiveSeed;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Dawwerli.Tests;

/// <summary>Own database with Seed:Live=true and small counts, so startup runs the live seed exactly like a server would.</summary>
public sealed class LiveSeedApiFactory : ApiFactory
{
    protected override string DatabaseSuffix => ";Database=dawwerli_test_live";

    protected override void ConfigureWebHost(IWebHostBuilder b)
    {
        base.ConfigureWebHost(b);
        b.UseSetting("Seed:Live", "true");
        b.UseSetting("Seed:Pages", "40");
        b.UseSetting("Seed:ProductPages", "500"); // above Pages = every page
        b.UseSetting("Seed:MinProductsPerPage", "50");
        b.UseSetting("Seed:MaxProductsPerPage", "50");
        b.UseSetting("Seed:Customers", "15");
        b.UseSetting("Seed:Offers", "12");
        b.UseSetting("Seed:StoreOrders", "40");
        b.UseSetting("Seed:Ads", "50");
        b.UseSetting("Seed:FeaturedPages", "3");
        b.UseSetting("Seed:SellerPassword", "seller-pass-123");
    }
}

public class LiveSeederTests(LiveSeedApiFactory factory) : IClassFixture<LiveSeedApiFactory>
{
    private async Task<(AsyncServiceScope Scope, AppDbContext Db, LiveDataSeeder Seeder, LiveSeedOptions Options)> OpenAsync()
    {
        await factory.ClientAsync(); // migrations + reference data + the startup live seed
        var scope = factory.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        return (scope, sp.GetRequiredService<AppDbContext>(), sp.GetRequiredService<LiveDataSeeder>(),
            sp.GetRequiredService<IOptions<LiveSeedOptions>>().Value.Copy());
    }

    [Fact]
    public async Task Startup_seed_writes_marker_and_configured_counts()
    {
        var (scope, db, seeder, o) = await OpenAsync();
        await using var _ = scope;
        Assert.True(await seeder.MarkerExistsAsync());
        Assert.Equal(o.Pages, await db.Pages.CountAsync());
        Assert.Equal(o.Customers, await db.Users.CountAsync(u => u.Role == UserRole.Customer));
        Assert.Equal(o.Offers, await db.Offers.CountAsync());
        Assert.Equal(o.Pages, await db.SellerAccounts.CountAsync());

        // Every page lists exactly 50 distinct products.
        var perPage = await db.Products.GroupBy(p => p.PageId).Select(g => new { Count = g.Count(), Names = g.Select(x => x.Name).Distinct().Count() }).ToListAsync();
        Assert.Equal(o.Pages, perPage.Count);
        Assert.All(perPage, n => { Assert.Equal(50, n.Count); Assert.Equal(50, n.Names); });
        // Almost all pages are verified and receiving; the few others are not receiving.
        Assert.True(await db.Pages.CountAsync(p => p.Status == PageStatus.Verified) >= o.Pages * 0.95);
        Assert.False(await db.Pages.AnyAsync(p => p.Receiving != (p.Status == PageStatus.Verified)));

        // The QA demo scenario must not be mixed into live data.
        Assert.False(await db.Pages.AnyAsync(p => p.InstagramHandle == "nahr.store"));
        // Fictional phone block for couriers; history is spread back in time but never in the future.
        Assert.All(await db.Couriers.Select(c => c.Phone).ToListAsync(), p => Assert.StartsWith("0790003", p));
        Assert.False(await db.Pages.AnyAsync(p => p.CreatedAt > DateTime.UtcNow));
        Assert.True(await db.Pages.MinAsync(p => p.CreatedAt) < DateTime.UtcNow.AddDays(-60));
        // Wallet balances match the ledger.
        var ledger = await db.LedgerEntries.GroupBy(l => l.PageId).Select(g => new { g.Key, Sum = g.Sum(x => x.Amount) }).ToDictionaryAsync(x => x.Key, x => x.Sum);
        Assert.All(await db.Pages.Select(p => new { p.Id, p.Balance }).ToListAsync(), p =>
        {
            Assert.Equal(LiveDataSeeder.PageBalance, p.Balance);
            Assert.Equal(ledger.GetValueOrDefault(p.Id), p.Balance);
        });
        // Every page customers can see has followers.
        var followed = await db.PageFollows.Select(f => f.PageId).Distinct().ToListAsync();
        Assert.All(await db.Pages.Where(p => p.Status == PageStatus.Verified || p.Status == PageStatus.Suspended).Select(p => p.Id).ToListAsync(),
            id => Assert.Contains(id, followed));
    }

    [Fact]
    public async Task Showcase_page_leads_products_offers_and_store_orders()
    {
        var (scope, db, _, _) = await OpenAsync();
        await using var __ = scope;
        var showcase = await db.Pages.SingleAsync(p => p.InstagramHandle == LiveDataSeeder.ShowcaseHandle);
        Assert.Equal(LiveDataSeeder.ShowcaseName, showcase.Name);
        Assert.True(showcase.IsActive(DateTime.UtcNow));
        Assert.True(showcase.Receiving);
        Assert.Equal(1, showcase.FeaturedOrder);

        var offers = await db.Offers.GroupBy(x => x.PageId).Select(g => new { g.Key, N = g.Count() }).ToListAsync();
        var orders = await db.StoreOrders.GroupBy(x => x.PageId).Select(g => new { g.Key, N = g.Count() }).ToListAsync();
        Assert.Equal(offers.Max(x => x.N), offers.Single(x => x.Key == showcase.Id).N);
        Assert.Equal(orders.Max(x => x.N), orders.Single(x => x.Key == showcase.Id).N);
        Assert.True(await db.Offers.CountAsync(x => x.PageId == showcase.Id && x.Status == OfferStatus.Selected) >= 3);
        Assert.True(await db.PageFollows.CountAsync(f => f.PageId == showcase.Id) > 0);
        Assert.True(await db.SellerAccounts.AnyAsync(a => a.PageId == showcase.Id && a.Email == LiveDataSeeder.ShowcaseSellerEmail));
        Assert.Equal([AdSlot.HomeBanner, AdSlot.FeaturedPage, AdSlot.SponsoredOffer],
            (await db.AdCampaigns.Where(a => a.PageId == showcase.Id).Select(a => a.Slot).ToListAsync()).Order());

        // The owner signs in to the app with the phone and the seeded password (no SMS needed).
        var c = factory.CreateClient();
        var login = await c.PostAsJsonAsync("/api/v1/auth/login", new { phone = LiveDataSeeder.ShowcasePhone, password = "seller-pass-123", role = "page", deviceId = "test-device" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task Every_page_owner_can_sign_in_with_phone_and_the_seed_password()
    {
        var (scope, db, _, o) = await OpenAsync();
        await using var __ = scope;
        var ownerIds = await db.Pages.Select(p => p.UserId).ToListAsync();
        Assert.Equal(o.Pages, ownerIds.Count);
        var hashes = await db.Users.Where(u => ownerIds.Contains(u.Id)).Select(u => u.PasswordHash).ToListAsync();
        Assert.Equal(o.Pages, hashes.Count);
        Assert.All(hashes, h => Assert.False(string.IsNullOrEmpty(h)));
        Assert.Single(hashes.Distinct()); // one precomputed hash for the shared password

        var c = factory.CreateClient();
        foreach (var n in new[] { 2, o.Pages / 2, o.Pages })
        {
            var login = await c.PostAsJsonAsync("/api/v1/auth/login", new { phone = $"0790002{n:D4}", password = "seller-pass-123", role = "page", deviceId = $"test-device-{n}" });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var wrong = await c.PostAsJsonAsync("/api/v1/auth/login", new { phone = $"0790002{n:D4}", password = "not-the-password", role = "page", deviceId = $"test-device-{n}" });
            Assert.NotEqual(HttpStatusCode.OK, wrong.StatusCode);
        }
    }

    [Fact]
    public async Task Ads_cover_every_slot_owner_and_status_and_are_paid_from_the_ledger()
    {
        var (scope, db, _, o) = await OpenAsync();
        await using var __ = scope;
        var ads = await db.AdCampaigns.AsNoTracking().ToListAsync();
        Assert.Equal(o.Ads, ads.Count);
        Assert.Equal(Enum.GetValues<AdSlot>().Order(), ads.Select(a => a.Slot).Distinct().Order());
        Assert.Equal(Enum.GetValues<AdOwnerType>().Order(), ads.Select(a => a.OwnerType).Distinct().Order());
        Assert.Equal(Enum.GetValues<AdStatus>().Order(), ads.Select(a => a.Status).Distinct().Order());
        var now = DateTime.UtcNow;
        Assert.True(ads.Count(a => a.Status == AdStatus.Active && a.StartAt <= now && a.EndAt > now) >= ads.Count / 2);
        Assert.Contains(ads, a => a.Status == AdStatus.Active && a.StartAt > now);
        // Brand and platform ads are home banners without a page; featured and sponsored ads always belong to a page.
        Assert.All(ads.Where(a => a.OwnerType != AdOwnerType.Page), a => { Assert.Equal(AdSlot.HomeBanner, a.Slot); Assert.Null(a.PageId); });
        Assert.All(ads.Where(a => a.OwnerType == AdOwnerType.Page), a => Assert.NotNull(a.PageId));
        Assert.All(ads.Where(a => a.Status == AdStatus.Ended), a => Assert.True(a.EndAt <= now));

        // Prepaid page ads carry an AdCharge entry; rejected ones were refunded; paused-for-balance ones never paid.
        var entries = await db.LedgerEntries.Where(e => e.RefId != null).ToListAsync();
        foreach (var a in ads.Where(a => a.PageId != null))
        {
            var charged = entries.Where(e => e.RefId == a.Id && e.Type == LedgerType.AdCharge).Sum(e => e.Amount);
            var refunded = entries.Where(e => e.RefId == a.Id && e.Type == LedgerType.AdRefund).Sum(e => e.Amount);
            if (a.Status == AdStatus.PausedBalance) { Assert.Equal(0, charged); Assert.Null(a.LastChargedOn); continue; }
            Assert.True(charged < 0);
            Assert.NotNull(a.LastChargedOn);
            if (a.Status == AdStatus.Rejected) Assert.Equal(0, charged + refunded);
        }

        // Sponsored offers follow the OfferService rule: one per request, pinned on the request.
        var sponsored = await db.Offers.Where(x => x.Sponsored).Select(x => new { x.Id, x.RequestId }).ToListAsync();
        Assert.NotEmpty(sponsored);
        var pinned = await db.Requests.Where(r => r.SponsoredOfferId != null).ToDictionaryAsync(r => r.Id, r => r.SponsoredOfferId);
        Assert.All(sponsored, x => Assert.Equal(x.Id, pinned[x.RequestId]));
    }

    [Fact]
    public async Task Catalog_has_every_category_slug_and_seeding_keeps_admin_edits()
    {
        var (scope, db, _, _) = await OpenAsync();
        await using var __ = scope;
        string[] slugs = ["phones", "shoes", "clothes", "perfumes", "watches", "care", "home", "electronics", "gifts", "furniture", "appliances",
            "gaming", "car-parts", "kids", "sports", "books", "beauty", "pharmacy", "food", "flowers", "pets", "tools"];
        var have = await db.Categories.Select(c => c.Slug).ToListAsync();
        Assert.All(slugs, s => Assert.Contains(s, have));

        var furniture = await db.Categories.SingleAsync(c => c.Slug == "furniture");
        furniture.Name = "أثاث ومفروشات";
        await db.SaveChangesAsync();
        var count = await db.Categories.CountAsync();
        await scope.ServiceProvider.GetRequiredService<Seeder>().RunAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(count, await db.Categories.CountAsync());
        var edited = await db.Categories.SingleAsync(c => c.Slug == "furniture");
        Assert.Equal("أثاث ومفروشات", edited.Name);
        edited.Name = "أثاث";
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Older_marker_version_triggers_one_reseed_on_startup()
    {
        var (scope, db, seeder, _) = await OpenAsync();
        await using var _ = scope;
        var marker = await db.Settings.SingleAsync(s => s.Id == LiveDataSeeder.MarkerId);
        marker.Data = marker.Data.Replace(LiveDataSeeder.MarkerName, "live-seed-v1");
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.False(await seeder.MarkerExistsAsync());

        var first = await seeder.RunOnStartupAsync();
        Assert.False(first.Skipped);
        Assert.True(await seeder.MarkerExistsAsync());
        var second = await seeder.RunOnStartupAsync();
        Assert.True(second.Skipped);
    }

    [Fact]
    public async Task Product_reviews_come_only_from_delivered_orders()
    {
        var (scope, db, _, _) = await OpenAsync();
        await using var __ = scope;
        var ratings = await db.ProductRatings.Select(r => new { r.ProductId, r.UserId, r.CreatedAt }).ToListAsync();
        Assert.NotEmpty(ratings);
        Assert.Equal(ratings.Count, ratings.Select(r => (r.ProductId, r.UserId)).Distinct().Count());
        var delivered = await (from so in db.StoreOrders
                               join i in db.StoreOrderItems on so.Id equals i.OrderId
                               where so.Status == StoreOrderStatus.Delivered
                               select new { so.CustomerUserId, i.ProductId, so.StatusChangedAt }).ToListAsync();
        Assert.All(ratings, r => Assert.Contains(delivered, d => d.CustomerUserId == r.UserId && d.ProductId == r.ProductId && d.StatusChangedAt <= r.CreatedAt));
        // Aggregates on products match the rows.
        var counts = ratings.GroupBy(r => r.ProductId).ToDictionary(g => g.Key, g => g.Count());
        Assert.All(await db.Products.Select(p => new { p.Id, p.RatingCount }).ToListAsync(), p => Assert.Equal(counts.GetValueOrDefault(p.Id), p.RatingCount));
    }

    [Fact]
    public async Task Marker_makes_startup_and_plain_seed_skip()
    {
        var (scope, db, seeder, o) = await OpenAsync();
        await using var _ = scope;
        var pages = await db.Pages.CountAsync();
        var startup = await seeder.RunOnStartupAsync();
        Assert.True(startup.Skipped);
        var noWipe = await seeder.RunAsync(o, wipe: false, onlyIfNoMarker: false);
        Assert.True(noWipe.Skipped);
        await scope.ServiceProvider.GetRequiredService<Seeder>().RunAsync();
        Assert.Equal(pages, await db.Pages.CountAsync());
    }

    [Fact]
    public async Task Every_table_is_either_wiped_or_explicitly_kept()
    {
        var (scope, db, _, _) = await OpenAsync();
        await using var _s = scope;
        var tables = await db.Database.SqlQueryRaw<string>(
            "SELECT table_name AS \"Value\" FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE'").ToListAsync();
        var covered = LiveDataSeeder.WipeSteps.Select(s => s.Label.Split(' ')[0]).Concat(LiveDataSeeder.KeptTables).ToHashSet();
        Assert.Empty(tables.Where(t => !covered.Contains(t)));
    }

    [Fact]
    public async Task Wipe_and_reseed_keeps_admins_settings_and_catalog()
    {
        var (scope, db, seeder, o) = await OpenAsync();
        await using var _ = scope;
        var c = factory.CreateClient();
        var admin = await Api.AdminAsync(c, factory);
        var adminIds = await db.Users.Where(u => u.Role == UserRole.Admin).Select(u => u.Id).OrderBy(x => x).ToListAsync();
        var govs = await db.Governorates.CountAsync();
        var cats = await db.Categories.CountAsync();
        var settings = await db.Settings.AsNoTracking().SingleAsync(s => s.Id == 1);

        var report = await seeder.RunAsync(o, wipe: true, onlyIfNoMarker: false);
        Assert.False(report.Skipped);
        Assert.Contains(report.Wiped, kv => kv.Key == "pages" && kv.Value == o.Pages);

        db.ChangeTracker.Clear();
        Assert.Equal(adminIds, await db.Users.Where(u => u.Role == UserRole.Admin).Select(u => u.Id).OrderBy(x => x).ToListAsync());
        Assert.Equal(o.Customers + o.Pages, await db.Users.CountAsync(u => u.Role != UserRole.Admin));
        Assert.Equal(govs, await db.Governorates.CountAsync());
        Assert.Equal(cats, await db.Categories.CountAsync());
        Assert.Equal(settings.Data, (await db.Settings.AsNoTracking().SingleAsync(s => s.Id == 1)).Data);
        Assert.Equal(o.Pages, await db.Pages.CountAsync());
        Assert.True(await seeder.MarkerExistsAsync());

        // The admin can still sign in and the public API serves the new data.
        var stillAdmin = new HttpRequestMessage(HttpMethod.Get, "/api/admin/v1/pages?status=pending");
        stillAdmin.Headers.Authorization = new("Bearer", admin);
        Assert.NotEqual(HttpStatusCode.Unauthorized, (await c.SendAsync(stillAdmin)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/v1/pages/directory")).StatusCode);
    }
}
