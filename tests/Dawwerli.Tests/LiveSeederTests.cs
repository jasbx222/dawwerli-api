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
        b.UseSetting("Seed:ProductPages", "10");
        b.UseSetting("Seed:MinProductsPerPage", "10");
        b.UseSetting("Seed:MaxProductsPerPage", "20");
        b.UseSetting("Seed:Customers", "15");
        b.UseSetting("Seed:Offers", "12");
        b.UseSetting("Seed:StoreOrders", "40");
        b.UseSetting("Seed:Banners", "2");
        b.UseSetting("Seed:FeaturedAds", "2");
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
        Assert.Equal(o.ProductPages, await db.SellerAccounts.CountAsync());

        var perPage = await db.Products.GroupBy(p => p.PageId).Select(g => g.Count()).ToListAsync();
        Assert.Equal(o.ProductPages, perPage.Count);
        Assert.All(perPage, n => Assert.InRange(n, o.MinProductsPerPage, o.MaxProductsPerPage));

        // The QA demo scenario must not be mixed into live data.
        Assert.False(await db.Pages.AnyAsync(p => p.InstagramHandle == "nahr.store"));
        // Fictional phone block for couriers; history is spread back in time but never in the future.
        Assert.All(await db.Couriers.Select(c => c.Phone).ToListAsync(), p => Assert.StartsWith("0790003", p));
        Assert.False(await db.Pages.AnyAsync(p => p.CreatedAt > DateTime.UtcNow));
        Assert.True(await db.Pages.MinAsync(p => p.CreatedAt) < DateTime.UtcNow.AddDays(-60));
        // Wallet balances match the ledger.
        var ledger = await db.LedgerEntries.GroupBy(l => l.PageId).Select(g => new { g.Key, Sum = g.Sum(x => x.Amount) }).ToDictionaryAsync(x => x.Key, x => x.Sum);
        Assert.All(await db.Pages.Select(p => new { p.Id, p.Balance }).ToListAsync(), p => Assert.Equal(ledger.GetValueOrDefault(p.Id), p.Balance));
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
