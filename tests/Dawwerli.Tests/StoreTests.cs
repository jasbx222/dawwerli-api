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
}
