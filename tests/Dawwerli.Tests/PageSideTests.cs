using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dawwerli.Domain.Entities;
using Dawwerli.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dawwerli.Tests;

/// <summary>Page app: home feed filters and paging, the page's own store orders, and offer states.</summary>
[Collection("api")]
public class PageSideTests(ApiFactory factory)
{
    private const int Clothes = 3, Perfumes = 4, Watches = 5;
    private const string Password = "seller-pass-123";

    private static HttpRequestMessage Req(string verb, string url, string? token, object? body = null)
    {
        var m = new HttpRequestMessage(new HttpMethod(verb), url);
        if (body is not null) m.Content = JsonContent.Create(body);
        if (token is not null) m.Headers.Authorization = new("Bearer", token);
        m.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return m;
    }

    private async Task<JsonElement> GetAsync(HttpClient c, string url, string token)
    {
        var r = await c.SendAsync(Req("GET", url, token));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await r.ReadAsync();
    }

    private static async Task<string> PostRequestAsync(HttpClient c, string customer, int categoryId, long maxPrice)
    {
        var r = await c.SendAsync(Req("POST", "/api/v1/requests", customer, new
        {
            mode = "photo", mediaKey = "requests/x.webp", description = "طلب للفلترة", categoryId, size = "L", condition = "new",
            maxPrice, governorateId = 1, readyToBuy = true,
        }));
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        return (await r.ReadAsync()).GetProperty("id").GetString()!;
    }

    private async Task ShiftCreatedAsync(string requestId, TimeSpan back)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = Guid.Parse(requestId);
        await db.Requests.Where(r => r.Id == id).ExecuteUpdateAsync(x => x.SetProperty(r => r.CreatedAt, r => r.CreatedAt - back));
    }

    private static List<string> Ids(JsonElement paged) =>
        paged.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()!).ToList();

    [Fact]
    public async Task Feed_filters_by_category_price_and_date_and_pages_results()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var page = await Api.VerifiedPageAsync(c, admin, Clothes);
        // A price band no other test uses, so the shared database does not leak into the counts.
        var band = 3_000_000L + Random.Shared.Next(1, 90_000) * 100L;
        var fresh = await PostRequestAsync(c, await Api.CustomerAsync(c), Clothes, band + 30);
        var weekOld = await PostRequestAsync(c, await Api.CustomerAsync(c), Clothes, band + 10);
        var monthOld = await PostRequestAsync(c, await Api.CustomerAsync(c), Clothes, band + 20);
        var perfume = await PostRequestAsync(c, await Api.CustomerAsync(c), Perfumes, band + 15);
        await ShiftCreatedAsync(weekOld, TimeSpan.FromDays(3));
        await ShiftCreatedAsync(monthOld, TimeSpan.FromDays(20));
        var range = $"minPrice={band}&maxPrice={band + 99}";

        var all = await GetAsync(c, $"/api/v1/pages/me/feed?{range}", page);
        Assert.Equal(4, all.GetProperty("total").GetInt32());
        Assert.Equal([perfume, fresh, weekOld, monthOld], Ids(all)); // newest first by default

        var clothes = await GetAsync(c, $"/api/v1/pages/me/feed?{range}&categoryId={Clothes}", page);
        Assert.Equal(3, clothes.GetProperty("total").GetInt32());
        Assert.All(clothes.GetProperty("items").EnumerateArray(), i => Assert.Equal(Clothes, i.GetProperty("categoryId").GetInt32()));

        Assert.Equal([weekOld, monthOld, fresh], Ids(await GetAsync(c, $"/api/v1/pages/me/feed?{range}&categoryId={Clothes}&sort=priceAsc", page)));
        Assert.Equal([fresh, monthOld, weekOld], Ids(await GetAsync(c, $"/api/v1/pages/me/feed?{range}&categoryId={Clothes}&sort=priceDesc", page)));
        Assert.Equal([monthOld, weekOld, fresh], Ids(await GetAsync(c, $"/api/v1/pages/me/feed?{range}&categoryId={Clothes}&sort=oldest", page)));

        Assert.Equal([fresh], Ids(await GetAsync(c, $"/api/v1/pages/me/feed?{range}&categoryId={Clothes}&since=today", page)));
        Assert.Equal([fresh, weekOld], Ids(await GetAsync(c, $"/api/v1/pages/me/feed?{range}&categoryId={Clothes}&since=week", page)));
        Assert.Equal(3, (await GetAsync(c, $"/api/v1/pages/me/feed?{range}&categoryId={Clothes}&since=month", page)).GetProperty("total").GetInt32());

        var narrow = await GetAsync(c, $"/api/v1/pages/me/feed?minPrice={band + 11}&maxPrice={band + 20}", page);
        Assert.Equal([perfume, monthOld], Ids(narrow));

        var p1 = await GetAsync(c, $"/api/v1/pages/me/feed?{range}&pageSize=3&page=1", page);
        var p2 = await GetAsync(c, $"/api/v1/pages/me/feed?{range}&pageSize=3&page=2", page);
        Assert.Equal(4, p2.GetProperty("total").GetInt32());
        Assert.Equal(3, p1.GetProperty("pageSize").GetInt32());
        Assert.Equal([perfume, fresh, weekOld], Ids(p1));
        Assert.Equal([monthOld], Ids(p2));
    }

    [Fact]
    public async Task Feed_hides_closed_requests_and_flags_my_offers()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var page = await Api.VerifiedPageAsync(c, admin, Clothes);
        var band = 4_000_000L + Random.Shared.Next(1, 90_000) * 100L;
        var open = await PostRequestAsync(c, await Api.CustomerAsync(c), Clothes, band + 1);
        var cancelled = await PostRequestAsync(c, await Api.CustomerAsync(c), Clothes, band + 2);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var id = Guid.Parse(cancelled);
            await db.Requests.Where(r => r.Id == id).ExecuteUpdateAsync(x => x.SetProperty(r => r.Status, RequestStatus.Cancelled));
        }
        Assert.Equal(HttpStatusCode.OK, (await Api.OfferAsync(c, page, open, band)).StatusCode);

        var feed = await GetAsync(c, $"/api/v1/pages/me/feed?minPrice={band}&maxPrice={band + 99}", page);
        var item = Assert.Single(feed.GetProperty("items").EnumerateArray());
        Assert.Equal(open, item.GetProperty("id").GetString());
        Assert.True(item.GetProperty("offeredByMe").GetBoolean());
    }

    [Theory]
    [InlineData("sort=cheapest", "sort.invalid")]
    [InlineData("since=year", "since.invalid")]
    [InlineData("minPrice=5000&maxPrice=1000", "price.range")]
    [InlineData("minPrice=-1", "price.invalid")]
    [InlineData("page=0", "paging.invalid")]
    [InlineData("categoryId=0", "categoryId.invalid")]
    public async Task Feed_rejects_bad_filters(string query, string code)
    {
        var c = await factory.ClientAsync();
        var page = await Api.VerifiedPageAsync(c, await Api.AdminAsync(c, factory), Clothes);
        var r = await c.SendAsync(Req("GET", $"/api/v1/pages/me/feed?{query}", page));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal(code, (await r.ReadAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Feed_needs_a_verified_page_and_clamps_page_size()
    {
        var c = await factory.ClientAsync();
        var pending = await Api.LoginAsync(c, "page");
        var handle = Guid.NewGuid().ToString("N")[..10];
        (await c.SendAsync(Req("POST", "/api/v1/pages/register", pending,
            new { name = $"p{handle}", instagram = $"p{handle}", categoryIds = new[] { Clothes }, governorateIds = new[] { 1 }, acceptTerms = true }))).EnsureSuccessStatusCode();
        var unverified = await c.SendAsync(Req("GET", "/api/v1/pages/me/feed", pending));
        Assert.False(unverified.IsSuccessStatusCode);
        var page = await Api.VerifiedPageAsync(c, await Api.AdminAsync(c, factory), Clothes);
        var big = await GetAsync(c, "/api/v1/pages/me/feed?pageSize=500", page);
        Assert.Equal(50, big.GetProperty("pageSize").GetInt32());
        var customer = await Api.CustomerAsync(c);
        Assert.NotEqual(HttpStatusCode.OK, (await c.SendAsync(Req("GET", "/api/v1/pages/me/feed", customer))).StatusCode);
    }

    // ---------- store orders ----------

    private sealed record Shop(string App, string Portal, string PageId);

    private async Task<Shop> ShopAsync(HttpClient c, string admin)
    {
        var app = await Api.VerifiedPageAsync(c, admin, Clothes);
        var pageId = (await GetAsync(c, "/api/v1/pages/me", app)).GetProperty("id").GetString()!;
        var email = $"s{Guid.NewGuid():N}@shop.test";
        (await c.SendAsync(Req("POST", "/api/admin/v1/seller-accounts", admin, new { pageId, email, password = Password }))).EnsureSuccessStatusCode();
        var login = await (await c.SendAsync(Req("POST", "/api/seller/v1/auth/login", null, new { email, password = Password }))).ReadAsync();
        return new Shop(app, login.GetProperty("accessToken").GetString()!, pageId);
    }

    private static async Task<string> OrderFromAsync(HttpClient c, Shop shop, string customer)
    {
        var p = await c.SendAsync(Req("POST", "/api/seller/v1/products", shop.Portal,
            new { name = "قميص قطن", description = "مقاس L", price = 25000, imageKeys = new[] { "products/a.webp" }, categoryId = Clothes, stock = 5 }));
        p.EnsureSuccessStatusCode();
        var productId = (await p.ReadAsync()).GetProperty("id").GetString();
        (await c.SendAsync(Req("POST", "/api/v1/cart/items", customer, new { productId, quantity = 2 }))).EnsureSuccessStatusCode();
        var r = await c.SendAsync(Req("POST", "/api/v1/store/checkout", customer, new { address = "بغداد - المنصور", paymentMethod = "cash" }));
        r.EnsureSuccessStatusCode();
        return (await r.ReadAsync()).GetProperty("orderId").GetString()!;
    }

    [Fact]
    public async Task Page_app_sees_and_moves_only_its_own_store_orders()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var mine = await ShopAsync(c, admin);
        var other = await ShopAsync(c, admin);
        var customer = await Api.CustomerAsync(c);
        var orderId = await OrderFromAsync(c, mine, customer);

        var list = await GetAsync(c, "/api/v1/pages/me/store-orders", mine.App);
        Assert.Equal(1, list.GetProperty("total").GetInt32());
        var row = list.GetProperty("items")[0];
        Assert.Equal(orderId, row.GetProperty("order").GetProperty("id").GetString());
        Assert.Equal("new", row.GetProperty("order").GetProperty("status").GetString());
        Assert.Equal(2, row.GetProperty("order").GetProperty("items")[0].GetProperty("quantity").GetInt32());
        Assert.False(string.IsNullOrEmpty(row.GetProperty("customerPhone").GetString()));

        Assert.Equal(0, (await GetAsync(c, "/api/v1/pages/me/store-orders", other.App)).GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(Req("GET", $"/api/v1/pages/me/store-orders/{orderId}", other.App))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await c.SendAsync(Req("POST", $"/api/v1/pages/me/store-orders/{orderId}/status", other.App, new { status = "accepted" }))).StatusCode);

        var bad = await c.SendAsync(Req("POST", $"/api/v1/pages/me/store-orders/{orderId}/status", mine.App, new { status = "delivered" }));
        Assert.False(bad.IsSuccessStatusCode);
        var ok = await c.SendAsync(Req("POST", $"/api/v1/pages/me/store-orders/{orderId}/status", mine.App, new { status = "accepted" }));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("accepted", (await ok.ReadAsync()).GetProperty("order").GetProperty("status").GetString());

        Assert.Equal(1, (await GetAsync(c, "/api/v1/pages/me/store-orders?status=active", mine.App)).GetProperty("total").GetInt32());
        Assert.Equal(1, (await GetAsync(c, "/api/v1/pages/me/store-orders?status=accepted", mine.App)).GetProperty("total").GetInt32());
        Assert.Equal(0, (await GetAsync(c, "/api/v1/pages/me/store-orders?status=closed", mine.App)).GetProperty("total").GetInt32());

        var cancel = await c.SendAsync(Req("POST", $"/api/v1/pages/me/store-orders/{orderId}/status", mine.App, new { status = "cancelled", reason = "نفذ المخزون" }));
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        var closed = await GetAsync(c, "/api/v1/pages/me/store-orders?status=closed", mine.App);
        Assert.Equal(1, closed.GetProperty("total").GetInt32());
        var done = closed.GetProperty("items")[0];
        Assert.Equal(JsonValueKind.Null, done.GetProperty("customerPhone").ValueKind); // phone hidden once the order is finished
        var detail = await GetAsync(c, $"/api/v1/pages/me/store-orders/{orderId}", mine.App);
        Assert.Equal(["new", "accepted", "cancelled"],
            detail.GetProperty("order").GetProperty("history").EnumerateArray().Select(h => h.GetProperty("status").GetString()).ToList());
        Assert.Equal("نفذ المخزون", detail.GetProperty("order").GetProperty("statusReason").GetString());

        var bogus = await c.SendAsync(Req("GET", "/api/v1/pages/me/store-orders?status=lost", mine.App));
        Assert.Equal(HttpStatusCode.BadRequest, bogus.StatusCode);
    }

    // ---------- offers ----------

    private static async Task<string> OfferIdAsync(HttpClient c, string page, string req, long price)
    {
        var r = await Api.OfferAsync(c, page, req, price);
        r.EnsureSuccessStatusCode();
        return (await r.ReadAsync()).GetProperty("id").GetString()!;
    }

    private static string StateOf(JsonElement list, string offerId) =>
        list.EnumerateArray().First(o => o.GetProperty("id").GetString() == offerId).GetProperty("state").GetString()!;

    [Fact]
    public async Task Offers_report_live_state_and_filter_by_state()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var winner = await Api.VerifiedPageAsync(c, admin, Watches);
        var loser = await Api.VerifiedPageAsync(c, admin, Watches);
        var customer = await Api.CustomerAsync(c);
        var req = await Api.RequestAsync(c, customer, Watches);
        var other = await Api.RequestAsync(c, await Api.CustomerAsync(c), Watches);
        var win = await OfferIdAsync(c, winner, req, 40000);
        var lose = await OfferIdAsync(c, loser, req, 45000);
        var pulled = await OfferIdAsync(c, loser, other, 30000);

        var before = await GetAsync(c, "/api/v1/pages/me/offers", loser);
        Assert.Equal("waiting", StateOf(before, lose));
        var sent = before.EnumerateArray().First(o => o.GetProperty("id").GetString() == lose);
        Assert.NotEqual(JsonValueKind.Null, sent.GetProperty("sentAt").ValueKind);
        Assert.Equal(60000, sent.GetProperty("requestMaxPrice").GetInt64());

        (await c.SendAsync(Req("POST", $"/api/v1/offers/{pulled}/withdraw", loser))).EnsureSuccessStatusCode();
        (await c.SendAsync(Req("POST", $"/api/v1/requests/{req}/select", customer, new { offerId = win }))).EnsureSuccessStatusCode();

        Assert.Equal("chosen", StateOf(await GetAsync(c, "/api/v1/pages/me/offers", winner), win));
        var after = await GetAsync(c, "/api/v1/pages/me/offers", loser);
        Assert.Equal("notChosen", StateOf(after, lose));
        Assert.Equal("withdrawn", StateOf(after, pulled));

        var notChosen = await GetAsync(c, "/api/v1/pages/me/offers?state=notChosen", loser);
        Assert.Equal([lose], notChosen.EnumerateArray().Select(o => o.GetProperty("id").GetString()).ToList());
        var withdrawn = await GetAsync(c, "/api/v1/pages/me/offers?state=withdrawn", loser);
        Assert.Equal([pulled], withdrawn.EnumerateArray().Select(o => o.GetProperty("id").GetString()).ToList());
        Assert.Empty((await GetAsync(c, "/api/v1/pages/me/offers?state=waiting", loser)).EnumerateArray());
        // Another page never sees these offers.
        Assert.DoesNotContain((await GetAsync(c, "/api/v1/pages/me/offers", winner)).EnumerateArray(), o => o.GetProperty("id").GetString() == lose);

        var bad = await c.SendAsync(Req("GET", "/api/v1/pages/me/offers?state=sold", loser));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal("state.invalid", (await bad.ReadAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Requests_made_inside_a_page_show_up_as_incoming_for_that_page_only()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var page = await Api.VerifiedPageAsync(c, admin, Watches);
        var other = await Api.VerifiedPageAsync(c, admin, Watches);
        var pageId = (await GetAsync(c, "/api/v1/pages/me", page)).GetProperty("id").GetString();
        var r = await c.SendAsync(Req("POST", "/api/v1/requests", await Api.CustomerAsync(c), new
        {
            mode = "photo", mediaKey = "requests/x.webp", description = "طلب للصفحة", categoryId = Watches, condition = "new",
            maxPrice = 70000, governorateId = 1, readyToBuy = true, pageId,
        }));
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        var id = (await r.ReadAsync()).GetProperty("id").GetString();

        var mine = await GetAsync(c, "/api/v1/pages/me/offers", page);
        Assert.Equal("incoming", mine.EnumerateArray().Single(o => o.GetProperty("id").GetString() == id).GetProperty("state").GetString());
        Assert.Single((await GetAsync(c, "/api/v1/pages/me/offers?state=incoming", page)).EnumerateArray());
        Assert.DoesNotContain((await GetAsync(c, "/api/v1/pages/me/offers", other)).EnumerateArray(), o => o.GetProperty("id").GetString() == id);
    }
}
