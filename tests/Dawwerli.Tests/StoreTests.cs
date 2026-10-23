using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dawwerli.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dawwerli.Tests;

[Collection("api")]
public class StoreTests(ApiFactory factory)
{
    private const int Category = 3;
    private const string Password = "seller-pass-123";

    private static HttpRequestMessage Req(string verb, string url, string? token, object? body = null)
    {
        var m = new HttpRequestMessage(new HttpMethod(verb), url);
        if (body is not null) m.Content = JsonContent.Create(body);
        if (token is not null) m.Headers.Authorization = new("Bearer", token);
        m.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return m;
    }

    private sealed record Seller(string PageId, string Token, string AccountId);

    /// <summary>Verified page + admin-created portal account, signed in on the web portal.</summary>
    private async Task<Seller> SellerAsync(HttpClient c, string admin)
    {
        var app = await Api.VerifiedPageAsync(c, admin, Category);
        var pageId = (await (await c.SendAsync(Req("GET", "/api/v1/pages/me", app))).ReadAsync()).GetProperty("id").GetString()!;
        var email = $"s{Guid.NewGuid():N}@shop.test";
        var acc = await c.SendAsync(Req("POST", "/api/admin/v1/seller-accounts", admin, new { pageId, email, password = Password }));
        acc.EnsureSuccessStatusCode();
        var accountId = (await acc.ReadAsync()).GetProperty("id").GetString()!;
        var login = await (await c.SendAsync(Req("POST", "/api/seller/v1/auth/login", null, new { email, password = Password }))).ReadAsync();
        return new Seller(pageId, login.GetProperty("accessToken").GetString()!, accountId);
    }

    private static async Task<HttpResponseMessage> CreateProductAsync(HttpClient c, string token, int stock = 5, string name = "قميص قطن", long price = 25000) =>
        await c.SendAsync(Req("POST", "/api/seller/v1/products", token, new { name, description = "مقاس L", price, imageKeys = new[] { "products/a.webp" }, categoryId = Category, stock }));

    private static async Task<string> ProductIdAsync(HttpClient c, string token, int stock = 5)
    {
        var r = await CreateProductAsync(c, token, stock);
        r.EnsureSuccessStatusCode();
        return (await r.ReadAsync()).GetProperty("id").GetString()!;
    }

    private static async Task<int> StockAsync(HttpClient c, string productId) =>
        (await (await c.SendAsync(Req("GET", $"/api/v1/products/{productId}", null))).ReadAsync()).GetProperty("stock").GetInt32();

    private static async Task<JsonElement> CheckoutAsync(HttpClient c, string customer, string productId, int qty)
    {
        (await c.SendAsync(Req("POST", "/api/v1/cart/items", customer, new { productId, quantity = qty }))).EnsureSuccessStatusCode();
        var r = await c.SendAsync(Req("POST", "/api/v1/store/checkout", customer, new { address = "بغداد - المنصور", paymentMethod = "cash" }));
        r.EnsureSuccessStatusCode();
        return await r.ReadAsync();
    }

    private static string Code(JsonElement e) => e.GetProperty("code").GetString()!;

    private async Task RateAsync(string pageId, double rating, int count)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var id = Guid.Parse(pageId);
        await db.Pages.Where(p => p.Id == id).ExecuteUpdateAsync(x => x.SetProperty(p => p.Rating, rating).SetProperty(p => p.RatingCount, count));
    }

    [Fact]
    public async Task Per_page_limit_override_is_enforced_on_create()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var s = await SellerAsync(c, admin);
        (await c.SendAsync(Req("PUT", $"/api/admin/v1/pages/{s.PageId}/product-limit", admin, new { limit = 2 }))).EnsureSuccessStatusCode();
        (await CreateProductAsync(c, s.Token)).EnsureSuccessStatusCode();
        (await CreateProductAsync(c, s.Token)).EnsureSuccessStatusCode();
        var third = await CreateProductAsync(c, s.Token);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, third.StatusCode);
        Assert.Equal("product.limit", Code(await third.ReadAsync()));
        var me = await (await c.SendAsync(Req("GET", "/api/seller/v1/me", s.Token))).ReadAsync();
        Assert.Equal(2, me.GetProperty("productLimit").GetInt32());
    }

    [Fact]
    public async Task Global_limit_from_admin_settings_applies_without_override()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var before = await (await c.SendAsync(Req("GET", "/api/admin/v1/store/settings", admin))).ReadAsync();
        try
        {
            (await c.SendAsync(Req("PUT", "/api/admin/v1/store/settings", admin, new { maxProductsPerPage = 1, commissionPercent = 0 }))).EnsureSuccessStatusCode();
            var s = await SellerAsync(c, admin);
            (await CreateProductAsync(c, s.Token)).EnsureSuccessStatusCode();
            Assert.Equal("product.limit", Code(await (await CreateProductAsync(c, s.Token)).ReadAsync()));
        }
        finally
        {
            await c.SendAsync(Req("PUT", "/api/admin/v1/store/settings", admin,
                new { maxProductsPerPage = before.GetProperty("maxProductsPerPage").GetInt32(), commissionPercent = before.GetProperty("commissionPercent").GetDouble() }));
        }
    }

    [Fact]
    public async Task Banned_words_are_rejected_in_product_text()
    {
        var c = await factory.ClientAsync();
        var s = await SellerAsync(c, await Api.AdminAsync(c, factory));
        var r = await CreateProductAsync(c, s.Token, name: "مسدس لعبة");
        Assert.Equal("product.prohibited", Code(await r.ReadAsync()));
    }

    [Fact]
    public async Task Cart_holds_products_from_one_page_only()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var a = await ProductIdAsync(c, (await SellerAsync(c, admin)).Token);
        var b = await ProductIdAsync(c, (await SellerAsync(c, admin)).Token);
        var customer = await Api.CustomerAsync(c);
        (await c.SendAsync(Req("POST", "/api/v1/cart/items", customer, new { productId = a, quantity = 1 }))).EnsureSuccessStatusCode();
        var other = await c.SendAsync(Req("POST", "/api/v1/cart/items", customer, new { productId = b, quantity = 1 }));
        Assert.Equal(HttpStatusCode.Conflict, other.StatusCode);
        Assert.Equal("cart.other_page", Code(await other.ReadAsync()));
    }

    [Fact]
    public async Task Stock_is_reserved_at_checkout_and_restored_on_reject_and_customer_cancel()
    {
        var c = await factory.ClientAsync();
        var s = await SellerAsync(c, await Api.AdminAsync(c, factory));
        var p = await ProductIdAsync(c, s.Token, stock: 3);
        var customer = await Api.CustomerAsync(c);

        var o1 = await CheckoutAsync(c, customer, p, 2);
        Assert.Equal(1, await StockAsync(c, p));
        var tooMany = await c.SendAsync(Req("POST", "/api/v1/cart/items", customer, new { productId = p, quantity = 2 }));
        Assert.Equal("product.stock", Code(await tooMany.ReadAsync()));

        (await c.SendAsync(Req("POST", $"/api/seller/v1/orders/{o1.GetProperty("orderId").GetString()}/status", s.Token, new { status = "rejected", reason = "نفد" })))
            .EnsureSuccessStatusCode();
        Assert.Equal(3, await StockAsync(c, p));

        var o2 = await CheckoutAsync(c, customer, p, 3);
        Assert.Equal(0, await StockAsync(c, p));
        var card = (await (await c.SendAsync(Req("GET", $"/api/v1/pages/{s.PageId}/products", null))).ReadAsync()).GetProperty("items")[0];
        Assert.False(card.GetProperty("available").GetBoolean());
        (await c.SendAsync(Req("POST", $"/api/v1/store/orders/{o2.GetProperty("orderId").GetString()}/cancel", customer))).EnsureSuccessStatusCode();
        Assert.Equal(3, await StockAsync(c, p));
    }

    [Fact]
    public async Task Status_transitions_follow_the_flow_and_customer_cancels_only_while_new()
    {
        var c = await factory.ClientAsync();
        var s = await SellerAsync(c, await Api.AdminAsync(c, factory));
        var p = await ProductIdAsync(c, s.Token);
        var customer = await Api.CustomerAsync(c);
        var id = (await CheckoutAsync(c, customer, p, 1)).GetProperty("orderId").GetString();
        Task<HttpResponseMessage> Move(string status) => c.SendAsync(Req("POST", $"/api/seller/v1/orders/{id}/status", s.Token, new { status }));

        Assert.Equal(HttpStatusCode.Conflict, (await Move("delivered")).StatusCode);
        (await Move("accepted")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await c.SendAsync(Req("POST", $"/api/v1/store/orders/{id}/cancel", customer))).StatusCode);
        (await Move("out_for_delivery")).EnsureSuccessStatusCode();
        (await Move("delivered")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await Move("cancelled")).StatusCode);

        var order = await (await c.SendAsync(Req("GET", $"/api/v1/store/orders/{id}", customer))).ReadAsync();
        Assert.Equal("delivered", order.GetProperty("status").GetString());
        var stats = await (await c.SendAsync(Req("GET", "/api/seller/v1/stats", s.Token))).ReadAsync();
        Assert.Equal(25000, stats.GetProperty("deliveredSales").GetInt64());
        Assert.Equal(1, stats.GetProperty("topProducts")[0].GetProperty("quantity").GetInt32());
    }

    [Fact]
    public async Task Portal_manages_couriers_assigns_one_on_dispatch_and_admin_can_read_them()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var s = await SellerAsync(c, admin);
        var other = await SellerAsync(c, admin);
        async Task<string> Add(string token, string name, string phone)
        {
            var r = await c.SendAsync(Req("POST", "/api/seller/v1/couriers", token, new { name, phone }));
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            return (await r.ReadAsync()).GetProperty("id").GetString()!;
        }
        var mine = await Add(s.Token, "حسن", "+9647801234567");
        var spare = await Add(s.Token, "كرار", "07701112233");
        var theirs = await Add(other.Token, "زيد", "07709998877");

        Assert.Equal(HttpStatusCode.BadRequest, (await c.SendAsync(Req("POST", "/api/seller/v1/couriers", s.Token, new { name = "x", phone = "123" }))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(Req("PUT", $"/api/seller/v1/couriers/{theirs}", s.Token, new { name = "x", phone = "07701234567" }))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(Req("DELETE", $"/api/seller/v1/couriers/{theirs}", s.Token))).StatusCode);
        (await c.SendAsync(Req("PUT", $"/api/seller/v1/couriers/{spare}", s.Token, new { name = "كرار", phone = "07701112233", active = false }))).EnsureSuccessStatusCode();

        var list = await (await c.SendAsync(Req("GET", "/api/seller/v1/couriers", s.Token))).ReadAsync();
        Assert.Equal(2, list.GetArrayLength());
        Assert.Equal("07801234567", list[0].GetProperty("phone").GetString());
        Assert.False(list[1].GetProperty("active").GetBoolean());

        var p = await ProductIdAsync(c, s.Token);
        var customer = await Api.CustomerAsync(c);
        var id = (await CheckoutAsync(c, customer, p, 1)).GetProperty("orderId").GetString();
        (await c.SendAsync(Req("POST", $"/api/seller/v1/orders/{id}/status", s.Token, new { status = "accepted" }))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(Req("POST", $"/api/seller/v1/orders/{id}/status", s.Token, new { status = "out_for_delivery", courierId = theirs }))).StatusCode);
        var sent = await c.SendAsync(Req("POST", $"/api/seller/v1/orders/{id}/status", s.Token, new { status = "out_for_delivery", courierId = mine }));
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        Assert.Equal("حسن", (await sent.ReadAsync()).GetProperty("order").GetProperty("courier").GetProperty("name").GetString());

        var seen = await (await c.SendAsync(Req("GET", $"/api/admin/v1/pages/{s.PageId}/couriers", admin))).ReadAsync();
        Assert.Equal(2, seen.GetArrayLength());
        Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(Req("GET", $"/api/admin/v1/pages/{Guid.NewGuid()}/couriers", admin))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(Req("GET", $"/api/admin/v1/pages/{s.PageId}/couriers", customer))).StatusCode);
        var orders = await (await c.SendAsync(Req("GET", $"/api/admin/v1/store/orders?pageId={s.PageId}", admin))).ReadAsync();
        Assert.Equal("07801234567", orders.GetProperty("items")[0].GetProperty("courier").GetProperty("phone").GetString());

        Assert.True((await c.SendAsync(Req("DELETE", $"/api/seller/v1/couriers/{mine}", s.Token))).IsSuccessStatusCode);
        Assert.Equal(1, (await (await c.SendAsync(Req("GET", $"/api/admin/v1/pages/{s.PageId}/couriers", admin))).ReadAsync()).GetArrayLength());
    }

    [Fact]
    public async Task Electronic_payment_without_provider_returns_not_configured_and_keeps_cart()
    {
        var c = await factory.ClientAsync();
        var s = await SellerAsync(c, await Api.AdminAsync(c, factory));
        var p = await ProductIdAsync(c, s.Token);
        var customer = await Api.CustomerAsync(c);
        (await c.SendAsync(Req("POST", "/api/v1/cart/items", customer, new { productId = p, quantity = 1 }))).EnsureSuccessStatusCode();
        var r = await c.SendAsync(Req("POST", "/api/v1/store/checkout", customer, new { address = "بغداد", paymentMethod = "electronic" }));
        Assert.Equal("payment.not_configured", Code(await r.ReadAsync()));
        Assert.Equal(5, await StockAsync(c, p));
        Assert.Equal(1, (await (await c.SendAsync(Req("GET", "/api/v1/cart", customer))).ReadAsync()).GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Portal_page_routes_act_for_the_sellers_own_page_only()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var s1 = await SellerAsync(c, admin);
        var s2 = await SellerAsync(c, admin);

        var me = await (await c.SendAsync(Req("GET", "/api/seller/v1/page", s1.Token))).ReadAsync();
        Assert.Equal(s1.PageId.ToString(), me.GetProperty("id").GetString());
        Assert.True(me.TryGetProperty("followerCount", out _));
        Assert.Equal(s2.PageId.ToString(), (await (await c.SendAsync(Req("GET", "/api/seller/v1/page", s2.Token))).ReadAsync()).GetProperty("id").GetString());

        foreach (var path in new[] { "wallet", "ledger", "ads", "ads/eligibility", "standing", "ratings", "offers", "inbox", "settings" })
            Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Req("GET", $"/api/seller/v1/page/{path}", s1.Token))).StatusCode);

        var settings = await (await c.SendAsync(Req("GET", "/api/seller/v1/page/settings", s1.Token))).ReadAsync();
        (await c.SendAsync(Req("PUT", "/api/seller/v1/page/settings", s1.Token, new
        {
            receiving = false, sound = true, categoryIds = new[] { Category }, governorateIds = new[] { 1 },
            from = settings.GetProperty("from").GetInt32(), to = settings.GetProperty("to").GetInt32(), days = new[] { 0, 1, 2 },
        }))).EnsureSuccessStatusCode();
        Assert.False((await (await c.SendAsync(Req("GET", "/api/seller/v1/page/settings", s1.Token))).ReadAsync()).GetProperty("receiving").GetBoolean());
        Assert.True((await (await c.SendAsync(Req("GET", "/api/seller/v1/page/settings", s2.Token))).ReadAsync()).GetProperty("receiving").GetBoolean());

        var appPage = await Api.VerifiedPageAsync(c, admin, Category);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(Req("GET", "/api/seller/v1/page/wallet", appPage))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Req("GET", "/api/seller/v1/page/wallet", null))).StatusCode);

        (await c.SendAsync(Req("POST", $"/api/admin/v1/seller-accounts/{s1.AccountId}/disable", admin))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Req("GET", "/api/seller/v1/page/wallet", s1.Token))).StatusCode);
    }

    [Fact]
    public async Task Portal_and_app_tokens_are_not_interchangeable_and_sellers_only_touch_their_own()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var s1 = await SellerAsync(c, admin);
        var s2 = await SellerAsync(c, admin);
        var p1 = await ProductIdAsync(c, s1.Token);

        var appPage = await Api.VerifiedPageAsync(c, admin, Category);
        Assert.Equal(HttpStatusCode.Forbidden, (await CreateProductAsync(c, appPage)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(Req("GET", "/api/v1/pages/me", s1.Token))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(Req("GET", "/api/admin/v1/products", s1.Token))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Req("GET", "/api/seller/v1/me", null))).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(Req("PUT", $"/api/seller/v1/products/{p1}", s2.Token,
            new { name = "x", description = "", price = 1, imageKeys = Array.Empty<string>(), categoryId = Category, stock = 1 }))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(Req("DELETE", $"/api/seller/v1/products/{p1}", s2.Token))).StatusCode);

        (await c.SendAsync(Req("POST", $"/api/admin/v1/seller-accounts/{s1.AccountId}/disable", admin))).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Req("GET", "/api/seller/v1/me", s1.Token))).StatusCode);
    }

    [Fact]
    public async Task Seller_login_locks_after_repeated_wrong_passwords_and_admin_reset_unlocks()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var app = await Api.VerifiedPageAsync(c, admin, Category);
        var pageId = (await (await c.SendAsync(Req("GET", "/api/v1/pages/me", app))).ReadAsync()).GetProperty("id").GetString();
        var email = $"l{Guid.NewGuid():N}@shop.test";
        var acc = await (await c.SendAsync(Req("POST", "/api/admin/v1/seller-accounts", admin, new { pageId, email, password = Password }))).ReadAsync();
        Task<HttpResponseMessage> Login(string pw) => c.SendAsync(Req("POST", "/api/seller/v1/auth/login", null, new { email, password = pw }));

        Assert.Equal(HttpStatusCode.Unauthorized, (await Login("wrong-wrong-1")).StatusCode);
        for (var i = 0; i < 4; i++) await Login("wrong-wrong-1");
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Login(Password)).StatusCode);
        (await c.SendAsync(Req("POST", $"/api/admin/v1/seller-accounts/{acc.GetProperty("id").GetString()}/reset-password", admin, new { password = "brand-new-pass-1" })))
            .EnsureSuccessStatusCode();
        (await Login("brand-new-pass-1")).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Admin_hides_product_and_valid_product_report_hides_it()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var s = await SellerAsync(c, admin);
        var p = await ProductIdAsync(c, s.Token);
        var customer = await Api.CustomerAsync(c);

        (await c.SendAsync(Req("POST", $"/api/v1/products/{p}/report", customer, new { reason = "fake", text = "مو أصلي" }))).EnsureSuccessStatusCode();
        var reports = await (await c.SendAsync(Req("GET", "/api/admin/v1/reports", admin))).ReadAsync();
        var report = reports.EnumerateArray().First(r => r.GetProperty("targetId").GetString() == p);
        Assert.Equal("product", report.GetProperty("target").GetString());
        (await c.SendAsync(Req("POST", $"/api/admin/v1/reports/{report.GetProperty("id").GetString()}/resolve", admin, new { accept = true, decision = "مخالف" })))
            .EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(Req("GET", $"/api/v1/products/{p}", null))).StatusCode);

        (await c.SendAsync(Req("POST", $"/api/admin/v1/products/{p}/unhide", admin))).EnsureSuccessStatusCode();
        (await c.SendAsync(Req("GET", $"/api/v1/products/{p}", null))).EnsureSuccessStatusCode();
        var audit = await (await c.SendAsync(Req("GET", "/api/admin/v1/audit", admin))).Content.ReadAsStringAsync();
        Assert.Contains("product.unhide", audit);
    }

    [Fact]
    public async Task Top_rated_products_come_from_the_best_rated_active_pages_and_skip_hidden_and_sold_out()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var best = await SellerAsync(c, admin);
        var second = await SellerAsync(c, admin);
        await RateAsync(best.PageId, 5.0, 900);
        await RateAsync(second.PageId, 5.0, 800);

        var newest = await ProductIdAsync(c, best.Token);
        var soldOut = await ProductIdAsync(c, best.Token, stock: 0);
        var hidden = await ProductIdAsync(c, best.Token);
        (await c.SendAsync(Req("POST", $"/api/admin/v1/products/{hidden}/hide", admin))).EnsureSuccessStatusCode();
        var other = await ProductIdAsync(c, second.Token);

        var r = await c.SendAsync(Req("GET", "/api/v1/products/top-rated?pages=2&perPage=3", null));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var items = (await r.ReadAsync()).EnumerateArray().ToList();
        var ids = items.Select(i => i.GetProperty("id").GetString()).ToList();
        Assert.Equal(new string?[] { newest, other }, ids);
        Assert.DoesNotContain(soldOut, ids);
        Assert.DoesNotContain(hidden, ids);
        var first = items[0];
        Assert.Equal(best.PageId, first.GetProperty("pageId").GetString());
        Assert.Equal(5.0, first.GetProperty("pageRating").GetDouble());
        Assert.True(first.GetProperty("available").GetBoolean());
        Assert.Equal(5, first.GetProperty("stock").GetInt32());
        Assert.Equal("مقاس L", first.GetProperty("description").GetString());

        // A page whose only product sells out drops off the strip.
        var customer = await Api.CustomerAsync(c);
        await CheckoutAsync(c, customer, other, 5);
        var after = (await (await c.SendAsync(Req("GET", "/api/v1/products/top-rated?pages=2&perPage=3", null))).ReadAsync())
            .EnumerateArray().Select(i => i.GetProperty("pageId").GetString()).ToList();
        Assert.DoesNotContain(second.PageId, after);
    }

    [Fact]
    public async Task Top_rated_products_skip_suspended_pages_and_cap_products_per_page()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var s = await SellerAsync(c, admin);
        await RateAsync(s.PageId, 5.0, 1000);
        for (var i = 0; i < 3; i++) await ProductIdAsync(c, s.Token);

        var capped = (await (await c.SendAsync(Req("GET", "/api/v1/products/top-rated?pages=1&perPage=2", null))).ReadAsync()).EnumerateArray().ToList();
        Assert.Equal(2, capped.Count);
        Assert.All(capped, p => Assert.Equal(s.PageId, p.GetProperty("pageId").GetString()));

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var id = Guid.Parse(s.PageId);
            await db.Pages.Where(p => p.Id == id).ExecuteUpdateAsync(x => x.SetProperty(p => p.SuspendedUntil, DateTime.UtcNow.AddDays(1)));
        }
        var after = (await (await c.SendAsync(Req("GET", "/api/v1/products/top-rated?pages=1&perPage=2", null))).ReadAsync())
            .EnumerateArray().Select(p => p.GetProperty("pageId").GetString()).ToList();
        Assert.DoesNotContain(s.PageId, after);
    }

    private static async Task DeliverAsync(HttpClient c, Seller s, string orderId)
    {
        foreach (var status in new[] { "accepted", "out_for_delivery", "delivered" })
            (await c.SendAsync(Req("POST", $"/api/seller/v1/orders/{orderId}/status", s.Token, new { status }))).EnsureSuccessStatusCode();
    }

    private static async Task<List<JsonElement>> FavoritesAsync(HttpClient c, string customer) =>
        (await (await c.SendAsync(Req("GET", "/api/v1/favorites", customer))).ReadAsync()).EnumerateArray().ToList();

    [Fact]
    public async Task Favorites_are_kept_per_customer_and_follow_the_product_state()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var s = await SellerAsync(c, admin);
        var kept = await ProductIdAsync(c, s.Token);
        var hidden = await ProductIdAsync(c, s.Token);
        var deleted = await ProductIdAsync(c, s.Token);
        var customer = await Api.CustomerAsync(c);
        var other = await Api.CustomerAsync(c);

        foreach (var p in new[] { kept, hidden, deleted, kept })
            Assert.Equal(HttpStatusCode.NoContent, (await c.SendAsync(Req("PUT", $"/api/v1/products/{p}/favorite", customer))).StatusCode);
        Assert.Equal(3, (await FavoritesAsync(c, customer)).Count);
        Assert.Empty(await FavoritesAsync(c, other));

        (await c.SendAsync(Req("POST", $"/api/admin/v1/products/{hidden}/hide", admin))).EnsureSuccessStatusCode();
        (await c.SendAsync(Req("DELETE", $"/api/seller/v1/products/{deleted}", s.Token))).EnsureSuccessStatusCode();
        var list = await FavoritesAsync(c, customer);
        Assert.Equal(new[] { hidden, kept }.Order(), list.Select(f => f.GetProperty("id").GetString()!).Order());
        Assert.False(list.Single(f => f.GetProperty("id").GetString() == hidden).GetProperty("available").GetBoolean());
        Assert.True(list.Single(f => f.GetProperty("id").GetString() == kept).GetProperty("available").GetBoolean());

        // Hidden products can't be newly favorited; removing is idempotent.
        Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(Req("PUT", $"/api/v1/products/{hidden}/favorite", other))).StatusCode);
        (await c.SendAsync(Req("DELETE", $"/api/v1/products/{kept}/favorite", customer))).EnsureSuccessStatusCode();
        (await c.SendAsync(Req("DELETE", $"/api/v1/products/{kept}/favorite", customer))).EnsureSuccessStatusCode();
        Assert.DoesNotContain(await FavoritesAsync(c, customer), f => f.GetProperty("id").GetString() == kept);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Req("GET", "/api/v1/favorites", null))).StatusCode);
    }

    [Fact]
    public async Task Only_customers_who_received_a_product_can_rate_it_once_and_edit_it()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var s = await SellerAsync(c, admin);
        var p = await ProductIdAsync(c, s.Token);
        var first = await Api.CustomerAsync(c);
        var second = await Api.CustomerAsync(c);
        Task<HttpResponseMessage> Rate(string token, int stars, string? comment = null) =>
            c.SendAsync(Req("PUT", $"/api/v1/products/{p}/rating", token, new { stars, comment }));

        // Placed but not delivered yet.
        var order = (await CheckoutAsync(c, first, p, 1)).GetProperty("orderId").GetString()!;
        Assert.Equal("rating.not_received", Code(await (await Rate(first, 5)).ReadAsync()));
        var mine = await (await c.SendAsync(Req("GET", $"/api/v1/products/{p}/ratings/mine", first))).ReadAsync();
        Assert.False(mine.GetProperty("canRate").GetBoolean());

        await DeliverAsync(c, s, order);
        await DeliverAsync(c, s, (await CheckoutAsync(c, second, p, 1)).GetProperty("orderId").GetString()!);
        Assert.True((await (await c.SendAsync(Req("GET", $"/api/v1/products/{p}/ratings/mine", first))).ReadAsync()).GetProperty("canRate").GetBoolean());

        Assert.Equal(HttpStatusCode.BadRequest, (await Rate(first, 6)).StatusCode);
        Assert.Equal("rating.prohibited", Code(await (await Rate(first, 5, "عنده سلاح")).ReadAsync()));
        (await Rate(first, 5, "  ممتاز  ")).EnsureSuccessStatusCode();
        var summary = await (await Rate(second, 2)).ReadAsync();
        Assert.Equal(3.5, summary.GetProperty("rating").GetDouble());
        Assert.Equal(2, summary.GetProperty("ratingCount").GetInt32());

        // Editing replaces the customer's rating instead of adding one.
        (await Rate(first, 3, "زين")).EnsureSuccessStatusCode();
        var details = await (await c.SendAsync(Req("GET", $"/api/v1/products/{p}", null))).ReadAsync();
        Assert.Equal(2.5, details.GetProperty("rating").GetDouble());
        Assert.Equal(2, details.GetProperty("ratingCount").GetInt32());
        var card = (await (await c.SendAsync(Req("GET", $"/api/v1/pages/{s.PageId}/products", null))).ReadAsync()).GetProperty("items")[0];
        Assert.Equal(2.5, card.GetProperty("rating").GetDouble());

        var reviews = await (await c.SendAsync(Req("GET", $"/api/v1/products/{p}/ratings", null))).ReadAsync();
        Assert.Equal(2, reviews.GetProperty("total").GetInt32());
        var latest = reviews.GetProperty("items")[0];
        Assert.Equal(3, latest.GetProperty("stars").GetInt32());
        Assert.Equal("زين", latest.GetProperty("comment").GetString());
        Assert.Equal("زبون", latest.GetProperty("customerName").GetString());
        var editedMine = await (await c.SendAsync(Req("GET", $"/api/v1/products/{p}/ratings/mine", first))).ReadAsync();
        Assert.Equal(3, editedMine.GetProperty("stars").GetInt32());

        var sellerList = await (await c.SendAsync(Req("GET", "/api/seller/v1/products", s.Token))).Content.ReadAsStringAsync();
        Assert.Contains("\"ratingCount\":2", sellerList);
    }

    // ---------- audit: authorization, races, validation ----------

    private async Task<int> DbAsync(Func<AppDbContext, Task<int>> work)
    {
        using var scope = factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private static string OrderId(JsonElement checkout) => checkout.GetProperty("orderId").GetString()!;

    [Fact]
    public async Task Customers_cannot_read_cancel_or_rate_another_customers_order()
    {
        var c = await factory.ClientAsync();
        var s = await SellerAsync(c, await Api.AdminAsync(c, factory));
        var p = await ProductIdAsync(c, s.Token);
        var owner = await Api.CustomerAsync(c);
        var stranger = await Api.CustomerAsync(c);
        var id = OrderId(await CheckoutAsync(c, owner, p, 1));

        Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(Req("GET", $"/api/v1/store/orders/{id}", stranger))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(Req("POST", $"/api/v1/store/orders/{id}/cancel", stranger))).StatusCode);
        Assert.Equal(0, (await (await c.SendAsync(Req("GET", "/api/v1/store/orders", stranger))).ReadAsync()).GetProperty("total").GetInt32());
        await DeliverAsync(c, s, id);
        Assert.Equal("rating.not_received", Code(await (await c.SendAsync(Req("PUT", $"/api/v1/products/{p}/rating", stranger, new { stars = 1 }))).ReadAsync()));
        Assert.Equal("delivered", (await (await c.SendAsync(Req("GET", $"/api/v1/store/orders/{id}", owner))).ReadAsync()).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Sellers_cannot_read_or_move_another_pages_orders()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var s1 = await SellerAsync(c, admin);
        var s2 = await SellerAsync(c, admin);
        var p = await ProductIdAsync(c, s1.Token);
        var id = OrderId(await CheckoutAsync(c, await Api.CustomerAsync(c), p, 1));

        Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(Req("GET", $"/api/seller/v1/orders/{id}", s2.Token))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(Req("POST", $"/api/seller/v1/orders/{id}/status", s2.Token, new { status = "rejected" }))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(Req("GET", $"/api/seller/v1/products/{p}", s2.Token))).StatusCode);
        Assert.Equal(0, (await (await c.SendAsync(Req("GET", "/api/seller/v1/orders", s2.Token))).ReadAsync()).GetProperty("total").GetInt32());
        Assert.Equal(4, await StockAsync(c, p));
    }

    [Fact]
    public async Task Racing_cancels_and_rejects_end_the_order_once_and_restore_stock_once()
    {
        var c = await factory.ClientAsync();
        var s = await SellerAsync(c, await Api.AdminAsync(c, factory));
        var p = await ProductIdAsync(c, s.Token, stock: 5);
        var customer = await Api.CustomerAsync(c);
        var id = OrderId(await CheckoutAsync(c, customer, p, 2));
        Assert.Equal(3, await StockAsync(c, p));

        var calls = Enumerable.Range(0, 10).Select(i => i % 2 == 0
            ? c.SendAsync(Req("POST", $"/api/v1/store/orders/{id}/cancel", customer))
            : c.SendAsync(Req("POST", $"/api/seller/v1/orders/{id}/status", s.Token, new { status = "rejected" })));
        var results = await Task.WhenAll(calls);
        Assert.Equal(1, results.Count(r => r.IsSuccessStatusCode));
        Assert.All(results.Where(r => !r.IsSuccessStatusCode), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal(5, await StockAsync(c, p));
    }

    [Fact]
    public async Task Concurrent_checkouts_never_oversell()
    {
        var c = await factory.ClientAsync();
        var s = await SellerAsync(c, await Api.AdminAsync(c, factory));
        var p = await ProductIdAsync(c, s.Token, stock: 3);
        var customers = new List<string>();
        for (var i = 0; i < 8; i++)
        {
            var t = await Api.CustomerAsync(c);
            (await c.SendAsync(Req("POST", "/api/v1/cart/items", t, new { productId = p, quantity = 1 }))).EnsureSuccessStatusCode();
            customers.Add(t);
        }
        var results = await Task.WhenAll(customers.Select(t =>
            c.SendAsync(Req("POST", "/api/v1/store/checkout", t, new { address = "بغداد", paymentMethod = "cash" }))));
        Assert.Equal(3, results.Count(r => r.IsSuccessStatusCode));
        foreach (var r in results.Where(r => !r.IsSuccessStatusCode))
        {
            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            Assert.Equal("product.stock", Code(await r.ReadAsync()));
        }
        Assert.Equal(0, await StockAsync(c, p));
    }

    [Fact]
    public async Task The_same_cart_checked_out_twice_at_once_places_one_order()
    {
        var c = await factory.ClientAsync();
        var s = await SellerAsync(c, await Api.AdminAsync(c, factory));
        var p = await ProductIdAsync(c, s.Token, stock: 5);
        var customer = await Api.CustomerAsync(c);
        (await c.SendAsync(Req("POST", "/api/v1/cart/items", customer, new { productId = p, quantity = 2 }))).EnsureSuccessStatusCode();

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            c.SendAsync(Req("POST", "/api/v1/store/checkout", customer, new { address = "بغداد", paymentMethod = "cash" }))));
        Assert.Equal(1, results.Count(r => r.IsSuccessStatusCode));
        Assert.Equal(3, await StockAsync(c, p));
        Assert.Equal(1, (await (await c.SendAsync(Req("GET", "/api/v1/store/orders", customer))).ReadAsync()).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Checkout_refuses_a_cart_that_ended_up_with_two_pages()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var a = await ProductIdAsync(c, (await SellerAsync(c, admin)).Token);
        var other = await SellerAsync(c, admin);
        var b = await ProductIdAsync(c, other.Token);
        var customer = await Api.CustomerAsync(c);
        (await c.SendAsync(Req("POST", "/api/v1/cart/items", customer, new { productId = a, quantity = 1 }))).EnsureSuccessStatusCode();
        var me = (await (await c.SendAsync(Req("GET", "/api/v1/me", customer))).ReadAsync()).GetProperty("id").GetString()!;
        // Two adds from different pages racing past the one-page check.
        await DbAsync(async db =>
        {
            db.CartItems.Add(new Domain.Entities.CartItem { UserId = Guid.Parse(me), PageId = Guid.Parse(other.PageId), ProductId = Guid.Parse(b), Quantity = 1 });
            return await db.SaveChangesAsync();
        });

        var r = await c.SendAsync(Req("POST", "/api/v1/store/checkout", customer, new { address = "بغداد", paymentMethod = "cash" }));
        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.Equal("cart.other_page", Code(await r.ReadAsync()));
        Assert.Equal(5, await StockAsync(c, a));
        Assert.Equal(5, await StockAsync(c, b));
    }

    [Fact]
    public async Task Prices_and_totals_come_from_the_database()
    {
        var c = await factory.ClientAsync();
        var s = await SellerAsync(c, await Api.AdminAsync(c, factory));
        var p = await ProductIdAsync(c, s.Token);
        var customer = await Api.CustomerAsync(c);
        (await c.SendAsync(Req("POST", "/api/v1/cart/items", customer, new { productId = p, quantity = 2, price = 1, unitPrice = 1 }))).EnsureSuccessStatusCode();
        var r = await c.SendAsync(Req("POST", "/api/v1/store/checkout", customer, new { address = "بغداد", paymentMethod = "cash", total = 1, subtotal = 1 }));
        var result = await r.ReadAsync();
        Assert.Equal(50000, result.GetProperty("total").GetInt64());
        var order = await (await c.SendAsync(Req("GET", $"/api/v1/store/orders/{OrderId(result)}", customer))).ReadAsync();
        Assert.Equal(25000, order.GetProperty("items")[0].GetProperty("unitPrice").GetInt64());
    }

    [Fact]
    public async Task Product_limit_holds_under_concurrent_creates()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var s = await SellerAsync(c, admin);
        (await c.SendAsync(Req("PUT", $"/api/admin/v1/pages/{s.PageId}/product-limit", admin, new { limit = 3 }))).EnsureSuccessStatusCode();
        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => CreateProductAsync(c, s.Token)));
        Assert.Equal(3, results.Count(r => r.IsSuccessStatusCode));
        Assert.Equal(3, (await (await c.SendAsync(Req("GET", "/api/seller/v1/products", s.Token))).ReadAsync()).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Banned_words_are_rejected_on_product_update_too()
    {
        var c = await factory.ClientAsync();
        var s = await SellerAsync(c, await Api.AdminAsync(c, factory));
        var p = await ProductIdAsync(c, s.Token);
        var r = await c.SendAsync(Req("PUT", $"/api/seller/v1/products/{p}", s.Token,
            new { name = "قميص", description = "يجي وياه مسدس", price = 1000, imageKeys = Array.Empty<string>(), categoryId = Category, stock = 1 }));
        Assert.Equal("product.prohibited", Code(await r.ReadAsync()));
    }

    [Fact]
    public async Task A_suspended_page_cannot_change_products_or_sell()
    {
        var c = await factory.ClientAsync();
        var s = await SellerAsync(c, await Api.AdminAsync(c, factory));
        var p = await ProductIdAsync(c, s.Token);
        var customer = await Api.CustomerAsync(c);
        var id = Guid.Parse(s.PageId);
        await DbAsync(db => db.Pages.Where(x => x.Id == id).ExecuteUpdateAsync(x => x.SetProperty(pg => pg.SuspendedUntil, DateTime.UtcNow.AddDays(1))));

        Assert.Equal("page.suspended", Code(await (await CreateProductAsync(c, s.Token)).ReadAsync()));
        Assert.Equal("page.suspended", Code(await (await c.SendAsync(Req("PUT", $"/api/seller/v1/products/{p}", s.Token,
            new { name = "قميص", description = "", price = 1000, imageKeys = Array.Empty<string>(), categoryId = Category, stock = 9 }))).ReadAsync()));
        Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(Req("POST", "/api/v1/cart/items", customer, new { productId = p, quantity = 1 }))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(Req("GET", $"/api/v1/products/{p}", null))).StatusCode);
        // Orders already placed can still be handled.
        (await c.SendAsync(Req("GET", "/api/seller/v1/orders", s.Token))).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Hidden_or_deleted_products_cannot_be_rated_bought_or_favorited()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var s = await SellerAsync(c, admin);
        var p = await ProductIdAsync(c, s.Token);
        var gone = await ProductIdAsync(c, s.Token);
        var customer = await Api.CustomerAsync(c);
        await DeliverAsync(c, s, OrderId(await CheckoutAsync(c, customer, p, 1)));
        await DeliverAsync(c, s, OrderId(await CheckoutAsync(c, customer, gone, 1)));

        (await c.SendAsync(Req("POST", $"/api/admin/v1/products/{p}/hide", admin))).EnsureSuccessStatusCode();
        (await c.SendAsync(Req("DELETE", $"/api/seller/v1/products/{gone}", s.Token))).EnsureSuccessStatusCode();
        foreach (var id in new[] { p, gone })
        {
            Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(Req("PUT", $"/api/v1/products/{id}/rating", customer, new { stars = 5 }))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(Req("PUT", $"/api/v1/products/{id}/favorite", customer))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(Req("POST", "/api/v1/cart/items", customer, new { productId = id, quantity = 1 }))).StatusCode);
            var mine = await c.SendAsync(Req("GET", $"/api/v1/products/{id}/ratings/mine", customer));
            if (mine.StatusCode != HttpStatusCode.NotFound) Assert.False((await mine.ReadAsync()).GetProperty("canRate").GetBoolean());
        }
    }

    [Fact]
    public async Task Sold_out_products_and_bad_quantities_are_refused()
    {
        var c = await factory.ClientAsync();
        var s = await SellerAsync(c, await Api.AdminAsync(c, factory));
        var soldOut = await ProductIdAsync(c, s.Token, stock: 0);
        var p = await ProductIdAsync(c, s.Token, stock: 5);
        var customer = await Api.CustomerAsync(c);
        Task<HttpResponseMessage> Add(string id, int quantity) => c.SendAsync(Req("POST", "/api/v1/cart/items", customer, new { productId = id, quantity }));

        Assert.Equal("product.unavailable", Code(await (await Add(soldOut, 1)).ReadAsync()));
        Assert.Equal(HttpStatusCode.BadRequest, (await Add(p, 0)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Add(p, -3)).StatusCode);
        Assert.Equal("cart.quantity", Code(await (await Add(p, 1000)).ReadAsync()));
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateProductAsync(c, s.Token, price: -5)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateProductAsync(c, s.Token, price: 1_000_000_000_000)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateProductAsync(c, s.Token, stock: -1)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateProductAsync(c, s.Token, name: new string('a', 500))).StatusCode);
    }

    [Fact]
    public async Task Free_text_and_paging_inputs_are_bounded()
    {
        var c = await factory.ClientAsync();
        var s = await SellerAsync(c, await Api.AdminAsync(c, factory));
        var p = await ProductIdAsync(c, s.Token);
        var customer = await Api.CustomerAsync(c);
        var id = OrderId(await CheckoutAsync(c, customer, p, 1));

        Assert.Equal(HttpStatusCode.BadRequest, (await c.SendAsync(Req("POST", $"/api/seller/v1/orders/{id}/status", s.Token,
            new { status = "rejected", reason = new string('x', 2000) }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.SendAsync(Req("POST", $"/api/v1/products/{p}/report", customer,
            new { reason = "fake", text = new string('x', 5000) }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.SendAsync(Req("POST", "/api/v1/store/checkout", customer,
            new { address = new string('x', 1000), paymentMethod = "cash" }))).StatusCode);
        var far = await c.SendAsync(Req("GET", $"/api/v1/pages/{s.PageId}/products?page={int.MaxValue}&pageSize=100000", null));
        Assert.Equal(HttpStatusCode.OK, far.StatusCode);
        Assert.Equal(50, (await far.ReadAsync()).GetProperty("pageSize").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Req("GET", $"/api/seller/v1/orders?page={int.MaxValue}", s.Token))).StatusCode);
    }

    [Fact]
    public async Task Admin_password_reset_signs_out_existing_seller_sessions()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var s = await SellerAsync(c, admin);
        (await c.SendAsync(Req("GET", "/api/seller/v1/me", s.Token))).EnsureSuccessStatusCode();
        (await c.SendAsync(Req("POST", $"/api/admin/v1/seller-accounts/{s.AccountId}/reset-password", admin, new { password = "another-pass-99" })))
            .EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Req("GET", "/api/seller/v1/me", s.Token))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await CreateProductAsync(c, s.Token)).StatusCode);
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient c, string token, string kind, string fileName, byte[] bytes)
    {
        var form = new MultipartFormDataContent { { new ByteArrayContent(bytes), "file", fileName }, { new StringContent(kind), "kind" } };
        var m = new HttpRequestMessage(HttpMethod.Post, "/api/seller/v1/media") { Content = form };
        m.Headers.Authorization = new("Bearer", token);
        return await c.SendAsync(m);
    }

    [Fact]
    public async Task Product_images_must_be_real_images()
    {
        var c = await factory.ClientAsync();
        var s = await SellerAsync(c, await Api.AdminAsync(c, factory));
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 0x49, 0x48, 0x44, 0x52];
        var html = "<html><script>alert(1)</script></html>"u8.ToArray();

        Assert.Equal("media.type", Code(await (await UploadAsync(c, s.Token, "products", "voice.mp3", [0x49, 0x44, 0x33, 4, 0, 0])).ReadAsync()));
        Assert.Equal("media.type", Code(await (await UploadAsync(c, s.Token, "products", "fake.png", html)).ReadAsync()));
        Assert.Equal("media.size", Code(await (await UploadAsync(c, s.Token, "products", "big.png", [.. png, .. new byte[11 * 1024 * 1024]])).ReadAsync()));
        var ok = await UploadAsync(c, s.Token, "products", "shirt.png", png);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.StartsWith("products/", (await ok.ReadAsync()).GetProperty("key").GetString());

        var receipt = await UploadAsync(c, s.Token, "receipts", "r.png", png);
        Assert.StartsWith("receipts/", (await receipt.ReadAsync()).GetProperty("key").GetString());
        Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync(c, s.Token, "disputes", "d.png", png)).StatusCode);
        Assert.Equal("media.type", Code(await (await UploadAsync(c, s.Token, "receipts", "r.pdf", "%PDF-1.4"u8.ToArray())).ReadAsync()));
    }

    [Fact]
    public async Task Seller_login_hides_whether_an_email_exists_and_hashes_passwords()
    {
        var c = await factory.ClientAsync();
        var s = await SellerAsync(c, await Api.AdminAsync(c, factory));
        var unknown = await c.SendAsync(Req("POST", "/api/seller/v1/auth/login", null, new { email = $"n{Guid.NewGuid():N}@shop.test", password = "wrong-wrong-1" }));
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal("auth.credentials", Code(await unknown.ReadAsync()));
        var id = Guid.Parse(s.AccountId);
        var hash = "";
        await DbAsync(async db => { hash = (await db.SellerAccounts.FirstAsync(a => a.Id == id)).PasswordHash; return 0; });
        Assert.DoesNotContain(Password, hash);
        Assert.Contains('.', hash);
    }

    [Fact]
    public async Task Order_history_lists_each_reached_status_with_its_time()
    {
        var c = await factory.ClientAsync();
        var s = await SellerAsync(c, await Api.AdminAsync(c, factory));
        var p = await ProductIdAsync(c, s.Token);
        var customer = await Api.CustomerAsync(c);
        var delivered = OrderId(await CheckoutAsync(c, customer, p, 1));
        await DeliverAsync(c, s, delivered);
        var cancelled = OrderId(await CheckoutAsync(c, customer, p, 1));
        (await c.SendAsync(Req("POST", $"/api/v1/store/orders/{cancelled}/cancel", customer))).EnsureSuccessStatusCode();

        async Task<List<JsonElement>> History(string id) =>
            (await (await c.SendAsync(Req("GET", $"/api/v1/store/orders/{id}", customer))).ReadAsync()).GetProperty("history").EnumerateArray().ToList();
        var steps = await History(delivered);
        Assert.Equal(["new", "accepted", "outForDelivery", "delivered"], steps.Select(e => e.GetProperty("status").GetString()!));
        var times = steps.Select(e => e.GetProperty("at").GetDateTime()).ToList();
        Assert.Equal(times.Order(), times);
        Assert.Equal(["new", "cancelled"], (await History(cancelled)).Select(e => e.GetProperty("status").GetString()!));
        var list = (await (await c.SendAsync(Req("GET", "/api/v1/store/orders", customer))).ReadAsync()).GetProperty("items");
        Assert.All(list.EnumerateArray(), o => Assert.True(o.GetProperty("history").GetArrayLength() >= 2));
    }

    [Fact]
    public async Task Unknown_seller_emails_lock_out_like_real_ones()
    {
        var c = await factory.ClientAsync();
        var email = $"ghost{Guid.NewGuid():N}@shop.test";
        Task<HttpResponseMessage> Login() => c.SendAsync(Req("POST", "/api/seller/v1/auth/login", null, new { email, password = "wrong-wrong-1" }));
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.Unauthorized, (await Login()).StatusCode);
        var locked = await Login();
        Assert.Equal(HttpStatusCode.TooManyRequests, locked.StatusCode);
        Assert.Equal("auth.locked", Code(await locked.ReadAsync()));
    }

    [Fact]
    public async Task Parallel_ratings_keep_the_average_exact_and_one_row_per_customer()
    {
        var c = await factory.ClientAsync();
        var s = await SellerAsync(c, await Api.AdminAsync(c, factory));
        var p = await ProductIdAsync(c, s.Token);
        var buyers = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            var b = await Api.CustomerAsync(c);
            await DeliverAsync(c, s, OrderId(await CheckoutAsync(c, b, p, 1)));
            buyers.Add(b);
        }
        Task<HttpResponseMessage> Rate(string token, int stars) =>
            c.SendAsync(Req("PUT", $"/api/v1/products/{p}/rating", token, new { stars }));

        // Same customer twice at once: both succeed, one rating row.
        var same = await Task.WhenAll(Rate(buyers[0], 5), Rate(buyers[0], 5));
        Assert.All(same, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        // Different customers at once: the stored average and count include everyone.
        var all = await Task.WhenAll(Rate(buyers[1], 1), Rate(buyers[2], 2), Rate(buyers[3], 4));
        Assert.All(all, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var details = await (await c.SendAsync(Req("GET", $"/api/v1/products/{p}", null))).ReadAsync();
        Assert.Equal(4, details.GetProperty("ratingCount").GetInt32());
        Assert.Equal(3.0, details.GetProperty("rating").GetDouble());
        var rows = 0;
        var id = Guid.Parse(p);
        await DbAsync(async db => rows = await db.ProductRatings.CountAsync(r => r.ProductId == id));
        Assert.Equal(4, rows);
    }

    // ---------- customer discovery, follows, rate-once, moderation ----------

    private static async Task<JsonElement> GetAsync(HttpClient c, string url, string? token = null)
    {
        var r = await c.SendAsync(Req("GET", url, token));
        r.EnsureSuccessStatusCode();
        return await r.ReadAsync();
    }

    [Fact]
    public async Task Best_selling_ranks_by_units_in_live_orders_and_highest_rated_by_product_rating()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var s = await SellerAsync(c, admin);
        var hot = await ProductIdAsync(c, s.Token, stock: 20);
        var warm = await ProductIdAsync(c, s.Token, stock: 20);
        var cancelled = await ProductIdAsync(c, s.Token, stock: 20);
        var customer = await Api.CustomerAsync(c);

        await DeliverAsync(c, s, (await CheckoutAsync(c, customer, hot, 4)).GetProperty("orderId").GetString()!);
        var accepted = (await CheckoutAsync(c, customer, warm, 2)).GetProperty("orderId").GetString()!;
        (await c.SendAsync(Req("POST", $"/api/seller/v1/orders/{accepted}/status", s.Token, new { status = "accepted" }))).EnsureSuccessStatusCode();
        var gone = (await CheckoutAsync(c, customer, cancelled, 9)).GetProperty("orderId").GetString()!;
        (await c.SendAsync(Req("POST", $"/api/v1/store/orders/{gone}/cancel", customer))).EnsureSuccessStatusCode();

        var best = (await GetAsync(c, "/api/v1/products/best-selling?pageSize=50")).GetProperty("items").EnumerateArray().ToList();
        var ids = best.Select(x => x.GetProperty("id").GetString()).ToList();
        Assert.Contains(hot, ids);
        Assert.Contains(warm, ids);
        Assert.DoesNotContain(cancelled, ids);
        Assert.True(ids.IndexOf(hot) < ids.IndexOf(warm));
        Assert.Equal(4, best.First(x => x.GetProperty("id").GetString() == hot).GetProperty("sold").GetInt32());

        (await c.SendAsync(Req("PUT", $"/api/v1/products/{hot}/rating", customer, new { stars = 5, comment = "حلو" }))).EnsureSuccessStatusCode();
        var rated = (await GetAsync(c, "/api/v1/products/highest-rated?pageSize=50")).GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("id").GetString()).ToList();
        Assert.Contains(hot, rated);
        Assert.DoesNotContain(warm, rated);
    }

    [Fact]
    public async Task A_product_is_rated_once_with_a_comment_and_a_second_rating_is_a_conflict()
    {
        var c = await factory.ClientAsync();
        var s = await SellerAsync(c, await Api.AdminAsync(c, factory));
        var p = await ProductIdAsync(c, s.Token);
        var customer = await Api.CustomerAsync(c);
        await DeliverAsync(c, s, (await CheckoutAsync(c, customer, p, 1)).GetProperty("orderId").GetString()!);

        var before = await GetAsync(c, $"/api/v1/products/{p}", customer);
        Assert.False(before.GetProperty("hasRated").GetBoolean());

        var first = await c.SendAsync(Req("POST", $"/api/v1/products/{p}/ratings", customer, new { stars = 4, comment = "  وصل بسرعة  " }));
        first.EnsureSuccessStatusCode();
        var again = await c.SendAsync(Req("POST", $"/api/v1/products/{p}/ratings", customer, new { stars = 1, comment = "مرة ثانية" }));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("rating.duplicate", Code(await again.ReadAsync()));

        var details = await GetAsync(c, $"/api/v1/products/{p}", customer);
        Assert.True(details.GetProperty("hasRated").GetBoolean());
        Assert.Equal(4, details.GetProperty("myReview").GetProperty("stars").GetInt32());
        Assert.Equal("وصل بسرعة", details.GetProperty("myReview").GetProperty("comment").GetString());
        Assert.Equal(1, details.GetProperty("ratingCount").GetInt32());

        var reviews = (await GetAsync(c, $"/api/v1/products/{p}/ratings")).GetProperty("items").EnumerateArray().ToList();
        Assert.Single(reviews);
        Assert.Equal("وصل بسرعة", reviews[0].GetProperty("comment").GetString());
        Assert.False(string.IsNullOrEmpty(reviews[0].GetProperty("customerName").GetString()));

        // Anonymous callers see the product without personal fields.
        Assert.False((await GetAsync(c, $"/api/v1/products/{p}")).GetProperty("hasRated").GetBoolean());
        // Rating needs a customer.
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Req("POST", $"/api/v1/products/{p}/ratings", null, new { stars = 5 }))).StatusCode);
    }

    [Fact]
    public async Task Admin_can_hide_a_review_from_the_list_and_the_average()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var s = await SellerAsync(c, admin);
        var p = await ProductIdAsync(c, s.Token);
        var a = await Api.CustomerAsync(c);
        var b = await Api.CustomerAsync(c);
        foreach (var (who, stars) in new[] { (a, 5), (b, 1) })
        {
            await DeliverAsync(c, s, (await CheckoutAsync(c, who, p, 1)).GetProperty("orderId").GetString()!);
            (await c.SendAsync(Req("POST", $"/api/v1/products/{p}/ratings", who, new { stars, comment = "تعليق" }))).EnsureSuccessStatusCode();
        }
        var list = (await GetAsync(c, $"/api/admin/v1/reviews?productId={p}", admin)).GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, list.Count);
        var bad = list.First(x => x.GetProperty("stars").GetInt32() == 1).GetProperty("id").GetString();
        (await c.SendAsync(Req("POST", $"/api/admin/v1/reviews/{bad}/hide", admin))).EnsureSuccessStatusCode();

        var details = await GetAsync(c, $"/api/v1/products/{p}");
        Assert.Equal(1, details.GetProperty("ratingCount").GetInt32());
        Assert.Equal(5, details.GetProperty("rating").GetDouble());
        Assert.Single((await GetAsync(c, $"/api/v1/products/{p}/ratings")).GetProperty("items").EnumerateArray());

        (await c.SendAsync(Req("POST", $"/api/admin/v1/reviews/{bad}/unhide", admin))).EnsureSuccessStatusCode();
        Assert.Equal(2, (await GetAsync(c, $"/api/v1/products/{p}")).GetProperty("ratingCount").GetInt32());
        // Customers can't moderate.
        Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(Req("POST", $"/api/admin/v1/reviews/{bad}/hide", a))).StatusCode);
    }

    [Fact]
    public async Task Following_a_page_is_idempotent_counted_and_shown_on_cards_profile_and_page_me()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var app = await Api.VerifiedPageAsync(c, admin, Category);
        var pageId = (await GetAsync(c, "/api/v1/pages/me", app)).GetProperty("id").GetString()!;
        var customer = await Api.CustomerAsync(c);
        var other = await Api.CustomerAsync(c);

        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Req("POST", $"/api/v1/pages/{pageId}/follow", null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(Req("POST", $"/api/v1/pages/{pageId}/follow", app))).StatusCode);

        var f1 = await (await c.SendAsync(Req("POST", $"/api/v1/pages/{pageId}/follow", customer))).ReadAsync();
        var f2 = await (await c.SendAsync(Req("POST", $"/api/v1/pages/{pageId}/follow", customer))).ReadAsync();
        Assert.True(f2.GetProperty("isFollowing").GetBoolean());
        Assert.Equal(1, f2.GetProperty("followerCount").GetInt32());
        (await c.SendAsync(Req("POST", $"/api/v1/pages/{pageId}/follow", other))).EnsureSuccessStatusCode();

        var profile = await GetAsync(c, $"/api/v1/pages/{pageId}/profile", customer);
        Assert.Equal(2, profile.GetProperty("followerCount").GetInt32());
        Assert.True(profile.GetProperty("isFollowing").GetBoolean());
        Assert.False((await GetAsync(c, $"/api/v1/pages/{pageId}/profile")).GetProperty("isFollowing").GetBoolean());
        Assert.Equal(2, (await GetAsync(c, "/api/v1/pages/me", app)).GetProperty("followerCount").GetInt32());

        var mine = (await GetAsync(c, "/api/v1/pages/directory?following=true&pageSize=50", customer)).GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(mine, x => x.GetProperty("id").GetString() == pageId && x.GetProperty("isFollowing").GetBoolean());

        var un = await (await c.SendAsync(Req("DELETE", $"/api/v1/pages/{pageId}/follow", customer))).ReadAsync();
        Assert.False(un.GetProperty("isFollowing").GetBoolean());
        Assert.Equal(1, un.GetProperty("followerCount").GetInt32());
        (await c.SendAsync(Req("DELETE", $"/api/v1/pages/{pageId}/follow", customer))).EnsureSuccessStatusCode();
        Assert.DoesNotContain((await GetAsync(c, "/api/v1/pages/directory?following=true&pageSize=50", customer)).GetProperty("items").EnumerateArray(),
            x => x.GetProperty("id").GetString() == pageId);
        Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(Req("POST", $"/api/v1/pages/{Guid.NewGuid()}/follow", customer))).StatusCode);
    }

    [Fact]
    public async Task Followers_get_one_throttled_notice_for_new_products_and_others_get_none()
    {
        var c = await factory.ClientAsync();
        var s = await SellerAsync(c, await Api.AdminAsync(c, factory));
        var follower = await Api.CustomerAsync(c);
        var stranger = await Api.CustomerAsync(c);
        (await c.SendAsync(Req("POST", $"/api/v1/pages/{s.PageId}/follow", follower))).EnsureSuccessStatusCode();

        var first = await ProductIdAsync(c, s.Token);
        await ProductIdAsync(c, s.Token);
        await ProductIdAsync(c, s.Token);

        List<JsonElement> Mine(JsonElement page) => page.GetProperty("items").EnumerateArray()
            .Where(n => n.GetProperty("route").GetString()!.Contains(s.PageId) || n.GetProperty("route").GetString() == $"/products/{first}").ToList();
        var notes = Mine(await GetAsync(c, "/api/v1/notifications", follower));
        Assert.Single(notes);
        Assert.Contains("3", notes[0].GetProperty("body").GetString());
        Assert.Equal($"/pages/{s.PageId}", notes[0].GetProperty("route").GetString());
        Assert.Empty(Mine(await GetAsync(c, "/api/v1/notifications", stranger)));
    }

    [Fact]
    public async Task Top_pages_puts_admin_picks_first_in_order_and_caps_at_five()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var picks = new List<string>();
        for (var i = 0; i < 2; i++)
        {
            var app = await Api.VerifiedPageAsync(c, admin, Category);
            picks.Add((await GetAsync(c, "/api/v1/pages/me", app)).GetProperty("id").GetString()!);
        }
        try
        {
            (await c.SendAsync(Req("PUT", "/api/admin/v1/pages/featured", admin, new { pageIds = new[] { picks[1], picks[0] } }))).EnsureSuccessStatusCode();
            var top = (await GetAsync(c, "/api/v1/pages/top")).EnumerateArray().Select(x => x.GetProperty("id").GetString()).ToList();
            Assert.True(top.Count <= 5);
            Assert.Equal(picks[1], top[0]);
            Assert.Equal(picks[0], top[1]);
            var featured = (await GetAsync(c, "/api/admin/v1/pages/featured", admin)).EnumerateArray().ToList();
            Assert.Equal(2, featured.Count);
            Assert.Equal(1, featured[0].GetProperty("order").GetInt32());

            var six = Enumerable.Range(0, 6).Select(_ => Guid.NewGuid()).ToArray();
            Assert.Equal(HttpStatusCode.BadRequest, (await c.SendAsync(Req("PUT", "/api/admin/v1/pages/featured", admin, new { pageIds = six }))).StatusCode);
            var dir = (await GetAsync(c, "/api/v1/pages/directory?pageSize=50")).GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetString()).ToList();
            Assert.Equal(picks[1], dir[0]);
        }
        finally
        {
            (await c.SendAsync(Req("PUT", "/api/admin/v1/pages/featured", admin, new { pageIds = Array.Empty<string>() }))).EnsureSuccessStatusCode();
        }
        Assert.Empty((await GetAsync(c, "/api/admin/v1/pages/featured", admin)).EnumerateArray());
    }
}
