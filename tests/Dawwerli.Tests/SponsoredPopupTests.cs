using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dawwerli.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Dawwerli.Tests;

/// <summary>Sponsored ads shown as the timed home popup: admin-editable timing, per-day pricing, image rule and tracking.</summary>
[Collection("api")]
public class SponsoredPopupTests(ApiFactory factory)
{
    private const int Clothes = 3;

    private static HttpRequestMessage Req(string verb, string url, string? token, object? body = null)
    {
        var m = new HttpRequestMessage(new HttpMethod(verb), url);
        if (body is not null) m.Content = JsonContent.Create(body);
        if (token is not null) m.Headers.Authorization = new("Bearer", token);
        m.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return m;
    }

    private static async Task<JsonElement> OkAsync(HttpResponseMessage r)
    {
        var text = await r.Content.ReadAsStringAsync();
        Assert.True(r.IsSuccessStatusCode, $"{(int)r.StatusCode} {text}");
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    [Fact]
    public async Task Public_config_and_pricing_expose_the_admin_popup_settings()
    {
        var c = await factory.ClientAsync();
        var customer = await Api.CustomerAsync(c);
        var cfg = await OkAsync(await c.SendAsync(Req("GET", "/api/v1/config", customer)));
        Assert.Equal(5, cfg.GetProperty("popupDurationSec").GetInt32());
        Assert.Equal(10, cfg.GetProperty("popupIntervalMin").GetInt32());
        Assert.Equal(3, cfg.GetProperty("popupFirstDelaySec").GetInt32());
        Assert.Equal(0, cfg.GetProperty("popupMaxPerAdPerDay").GetInt32());

        var admin = await Api.AdminAsync(c, factory);
        var page = await Api.VerifiedPageAsync(c, admin, Clothes);
        var pricing = await OkAsync(await c.SendAsync(Req("GET", "/api/v1/pages/me/ads/pricing", page)));
        Assert.Equal(cfg.GetProperty("sponsoredOffer").GetInt64(), pricing.GetProperty("sponsoredOffer").GetInt64());
        Assert.Equal(5, pricing.GetProperty("popupDurationSec").GetInt32());
    }

    [Fact]
    public async Task Admin_edits_popup_timing_and_bad_values_are_rejected()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var rules = (await OkAsync(await c.SendAsync(Req("GET", "/api/admin/v1/settings", admin)))).GetRawText();

        JsonObject With(string key, int value) { var o = JsonNode.Parse(rules)!.AsObject(); o[key] = value; return o; }
        foreach (var (key, bad) in new[] { ("popupDurationSec", 0), ("popupIntervalMin", 0), ("popupFirstDelaySec", -1), ("popupMaxPerAdPerDay", -1) })
            Assert.Equal(HttpStatusCode.BadRequest, (await c.SendAsync(Req("PUT", "/api/admin/v1/settings", admin, With(key, bad)))).StatusCode);

        try
        {
            await OkAsync(await c.SendAsync(Req("PUT", "/api/admin/v1/settings", admin, With("popupIntervalMin", 25))));
            var customer = await Api.CustomerAsync(c);
            var cfg = await OkAsync(await c.SendAsync(Req("GET", "/api/v1/config", customer)));
            Assert.Equal(25, cfg.GetProperty("popupIntervalMin").GetInt32());
        }
        finally
        {
            await OkAsync(await c.SendAsync(Req("PUT", "/api/admin/v1/settings", admin, JsonNode.Parse(rules))));
        }
    }

    [Fact]
    public async Task Sponsored_ad_needs_an_image_is_billed_per_day_and_counts_events()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var page = await Api.VerifiedPageAsync(c, admin, Clothes);
        var daily = (await OkAsync(await c.SendAsync(Req("GET", "/api/v1/pages/me/ads/pricing", page)))).GetProperty("sponsoredOffer").GetInt64();
        object Ad(string? image) => new
        {
            slot = "sponsoredOffer", title = "عرض الجمعة", headline = "قمصان قطن بسعر حلو", discountPercent = 20, governorateIds = new[] { 1 },
            categoryIds = Array.Empty<int>(), startAt = DateTime.UtcNow.AddMinutes(-1), days = 3, imageKey = image, deepLink = (string?)null,
        };

        Assert.Equal(HttpStatusCode.BadRequest, (await c.SendAsync(Req("POST", "/api/v1/pages/me/ads", page, Ad(null)))).StatusCode);

        var before = (await OkAsync(await c.SendAsync(Req("GET", "/api/v1/pages/me/wallet", page)))).GetProperty("balance").GetInt64();
        var ad = await OkAsync(await c.SendAsync(Req("POST", "/api/v1/pages/me/ads", page, Ad("ads/popup.webp"))));
        var id = ad.GetProperty("id").GetString()!;
        Assert.Equal(daily, ad.GetProperty("dailyPrice").GetInt64());
        var after = (await OkAsync(await c.SendAsync(Req("GET", "/api/v1/pages/me/wallet", page)))).GetProperty("balance").GetInt64();
        Assert.Equal(daily * 3, before - after);

        await OkAsync(await c.SendAsync(Req("POST", $"/api/admin/v1/ads/{id}/approve", admin)));
        var served = await OkAsync(await c.GetAsync("/api/v1/ads?slot=sponsoredOffer&governorateId=1"));
        var mine = served.EnumerateArray().Single(a => a.GetProperty("id").GetString() == id);
        Assert.NotNull(mine.GetProperty("image").GetString());
        Assert.True(mine.GetProperty("pageVerified").GetBoolean());

        foreach (var type in new[] { "impression", "click", "impression" })
            await OkAsync(await c.SendAsync(Req("POST", $"/api/v1/ads/{id}/events", null, new { type, deviceId = "popup-test" })));
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.AdCampaigns.AsNoTracking().SingleAsync(a => a.Id == Guid.Parse(id));
        Assert.Equal(1, row.Impressions);
        Assert.Equal(1, row.Clicks);
    }

    [Fact]
    public async Task Migration_adds_popup_defaults_and_keeps_admin_values()
    {
        await factory.ClientAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var migrator = db.GetInfrastructure().GetRequiredService<IMigrator>();
        var all = db.Database.GetMigrations().ToList();
        var mine = all.Single(m => m.EndsWith("_SponsoredAdPopupSettings"));
        var previous = all[all.IndexOf(mine) - 1];
        var original = await db.Settings.AsNoTracking().Where(s => s.Id == 1).Select(s => s.Data).SingleAsync();
        try
        {
            await migrator.MigrateAsync(previous);
            await db.Database.ExecuteSqlRawAsync("""UPDATE settings SET data = jsonb_set(data, '{{BannerDaily}}', '7777') WHERE id = 1""");
            var down = JsonNode.Parse(await db.Settings.AsNoTracking().Where(s => s.Id == 1).Select(s => s.Data).SingleAsync())!.AsObject();
            Assert.False(down.ContainsKey("PopupDurationSec"));

            await migrator.MigrateAsync(mine);
            var up = JsonNode.Parse(await db.Settings.AsNoTracking().Where(s => s.Id == 1).Select(s => s.Data).SingleAsync())!.AsObject();
            Assert.Equal(5, up["PopupDurationSec"]!.GetValue<int>());
            Assert.Equal(10, up["PopupIntervalMin"]!.GetValue<int>());
            Assert.Equal(3, up["PopupFirstDelaySec"]!.GetValue<int>());
            Assert.Equal(0, up["PopupMaxPerAdPerDay"]!.GetValue<int>());
            Assert.Equal(7777, up["BannerDaily"]!.GetValue<long>());
        }
        finally
        {
            await db.Database.MigrateAsync();
            await db.Database.ExecuteSqlRawAsync("UPDATE settings SET data = {0}::jsonb WHERE id = 1", original);
        }
    }
}
