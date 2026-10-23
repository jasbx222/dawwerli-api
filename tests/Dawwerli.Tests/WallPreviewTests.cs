using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dawwerli.Application.Validation;
using Dawwerli.Domain.Entities;

namespace Dawwerli.Tests;

/// <summary>Unit rules behind «حطها على حايطك»: size format, wall kinds and variant keys.</summary>
public class WallSizeRulesTests
{
    [Theory]
    [InlineData("1", true)]
    [InlineData("1000", true)]
    [InlineData("55.5", true)]
    [InlineData("145.1", true)]
    [InlineData("0.9", false)]
    [InlineData("0", false)]
    [InlineData("-3", false)]
    [InlineData("1000.1", false)]
    [InlineData("55.55", false)]
    [InlineData("12.05", false)]
    public void Size_is_1_to_1000_cm_with_one_decimal(string text, bool ok) =>
        Assert.Equal(ok, ProductSize.IsValidCm(decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("tv", true, WallKind.Tv)]
    [InlineData("Mirror", true, WallKind.Mirror)]
    [InlineData(" rug ", true, WallKind.Rug)]
    [InlineData("1", false, default(WallKind))]
    [InlineData("", false, default(WallKind))]
    [InlineData("sofa", false, default(WallKind))]
    [InlineData("tv,mirror", false, default(WallKind))]
    public void Wall_kind_accepts_codes_only(string text, bool ok, WallKind expected)
    {
        Assert.Equal(ok, ProductSize.TryParseWallKind(text, out var kind));
        if (ok) Assert.Equal(expected, kind);
    }

    [Theory]
    [InlineData(" TV-Smart_4K ", "tv-smart_4k")]
    [InlineData("a", "a")]
    [InlineData("", null)]
    [InlineData("has space", null)]
    [InlineData("شاشة", null)]
    [InlineData("../x", null)]
    [InlineData("0123456789012345678901234567890123456789x", null)]
    public void Variant_group_is_a_short_ascii_key(string text, string? expected) =>
        Assert.Equal(expected, ProductSize.NormalizeGroup(text));
}

/// <summary>API side of wall-placeable products: validation, ownership and page-scoped variant groups.</summary>
[Collection("api")]
public class WallPreviewTests(ApiFactory factory)
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

    private sealed record Seller(string PageId, string Token);

    private static async Task<Seller> SellerAsync(HttpClient c, string admin)
    {
        var app = await Api.VerifiedPageAsync(c, admin, Category);
        var pageId = (await (await c.SendAsync(Req("GET", "/api/v1/pages/me", app))).ReadAsync()).GetProperty("id").GetString()!;
        var email = $"w{Guid.NewGuid():N}@shop.test";
        (await c.SendAsync(Req("POST", "/api/admin/v1/seller-accounts", admin, new { pageId, email, password = Password }))).EnsureSuccessStatusCode();
        var login = await (await c.SendAsync(Req("POST", "/api/seller/v1/auth/login", null, new { email, password = Password }))).ReadAsync();
        return new Seller(pageId, login.GetProperty("accessToken").GetString()!);
    }

    private static object Body(string name = "شاشة", long price = 500_000, decimal? widthCm = null, decimal? heightCm = null, decimal? depthCm = null,
        string? wallKind = null, string? variantGroup = null, string? variantLabel = null, bool active = true) =>
        new { name, description = "", price, imageKeys = new[] { "products/tv.webp" }, categoryId = Category, stock = 4, active,
            widthCm, heightCm, depthCm, wallKind, variantGroup, variantLabel };

    private static async Task<string> CreateAsync(HttpClient c, string token, object body)
    {
        var r = await c.SendAsync(Req("POST", "/api/seller/v1/products", token, body));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await r.ReadAsync()).GetProperty("id").GetString()!;
    }

    private static async Task<JsonElement> DetailsAsync(HttpClient c, string id) =>
        await (await c.SendAsync(Req("GET", $"/api/v1/products/{id}", null))).ReadAsync();

    private static List<string> VariantIds(JsonElement details) =>
        details.GetProperty("variants").EnumerateArray().Select(v => v.GetProperty("id").GetString()!).ToList();

    [Fact]
    public async Task Sizes_kind_and_group_round_trip_to_seller_card_and_details()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var s = await SellerAsync(c, admin);
        var id = await CreateAsync(c, s.Token, Body(widthCm: 123.1m, heightCm: 70.9m, depthCm: 5.9m, wallKind: "TV", variantGroup: " TV-55 ", variantLabel: " 55 بوصة "));

        var mine = await (await c.SendAsync(Req("GET", $"/api/seller/v1/products/{id}", s.Token))).ReadAsync();
        Assert.Equal(123.1m, mine.GetProperty("widthCm").GetDecimal());
        Assert.Equal(70.9m, mine.GetProperty("heightCm").GetDecimal());
        Assert.Equal(5.9m, mine.GetProperty("depthCm").GetDecimal());
        Assert.Equal("tv", mine.GetProperty("wallKind").GetString());
        Assert.Equal("tv-55", mine.GetProperty("variantGroup").GetString());
        Assert.Equal("55 بوصة", mine.GetProperty("variantLabel").GetString());

        var d = await DetailsAsync(c, id);
        Assert.Equal(123.1m, d.GetProperty("widthCm").GetDecimal());
        Assert.Equal("tv", d.GetProperty("wallKind").GetString());
        Assert.Empty(d.GetProperty("variants").EnumerateArray()); // alone in its group
        Assert.False(d.TryGetProperty("variantGroup", out _)); // the key stays seller-side
        var raw = (await (await c.SendAsync(Req("GET", $"/api/v1/products/{id}", null))).Content.ReadAsStringAsync()).ToLowerInvariant();
        Assert.DoesNotContain("phone", raw);

        var cards = await (await c.SendAsync(Req("GET", $"/api/v1/pages/{s.PageId}/products", null))).ReadAsync();
        var card = cards.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("id").GetString() == id);
        Assert.Equal(70.9m, card.GetProperty("heightCm").GetDecimal());
        Assert.Equal("tv", card.GetProperty("wallKind").GetString());

        // A PUT without the optional fields (or with blank ones) clears them (full replace, like every other field).
        (await c.SendAsync(Req("PUT", $"/api/seller/v1/products/{id}", s.Token, Body(wallKind: " ", variantGroup: "", variantLabel: "")))).EnsureSuccessStatusCode();
        Assert.Equal(JsonValueKind.Null, (await (await c.SendAsync(Req("GET", $"/api/seller/v1/products/{id}", s.Token))).ReadAsync()).GetProperty("variantGroup").ValueKind);
        (await c.SendAsync(Req("PUT", $"/api/seller/v1/products/{id}", s.Token, Body()))).EnsureSuccessStatusCode();
        var cleared = await DetailsAsync(c, id);
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("widthCm").ValueKind);
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("wallKind").ValueKind);
    }

    public static TheoryData<string, object> BadBodies => new()
    {
        { "widthCm", Body(widthCm: 55.55m, heightCm: 40m) },
        { "widthCm", Body(widthCm: 0.5m, heightCm: 40m) },
        { "heightCm", Body(widthCm: 50m, heightCm: 1000.5m) },
        { "heightCm", Body(widthCm: 50m) },
        { "widthCm", Body(heightCm: 50m) },
        { "widthCm", Body(depthCm: 5m) },
        { "widthCm", Body(wallKind: "tv") },
        { "depthCm", Body(widthCm: 50m, heightCm: 40m, depthCm: 0m) },
        { "wallKind", Body(widthCm: 50m, heightCm: 40m, wallKind: "1") },
        { "wallKind", Body(widthCm: 50m, heightCm: 40m, wallKind: "sofa") },
        { "variantGroup", Body(variantGroup: "bad key!", variantLabel: "x") },
        { "variantLabel", Body(variantGroup: "ok-key") },
        { "variantLabel", Body(variantGroup: "ok-key", variantLabel: new string('x', 31)) },
    };

    [Theory]
    [MemberData(nameof(BadBodies))]
    public async Task Bad_sizes_kinds_and_groups_are_rejected(string field, object body)
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var s = await SellerAsync(c, admin);
        var r = await c.SendAsync(Req("POST", "/api/seller/v1/products", s.Token, body));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.True((await r.ReadAsync()).GetProperty("errors").TryGetProperty(field, out _), $"expected an error on {field}");
    }

    [Fact]
    public async Task Another_page_cannot_change_or_read_sizes_of_my_product()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var s1 = await SellerAsync(c, admin);
        var s2 = await SellerAsync(c, admin);
        var id = await CreateAsync(c, s1.Token, Body(widthCm: 60m, heightCm: 90m, wallKind: "mirror"));

        Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(Req("PUT", $"/api/seller/v1/products/{id}", s2.Token,
            Body(widthCm: 999m, heightCm: 999m, wallKind: "tv")))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(Req("GET", $"/api/seller/v1/products/{id}", s2.Token))).StatusCode);
        // Customer and page-app tokens can't use the seller portal at all.
        var customer = await Api.LoginAsync(c, "customer");
        Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(Req("PUT", $"/api/seller/v1/products/{id}", customer, Body(widthCm: 1m, heightCm: 1m)))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Req("PUT", $"/api/seller/v1/products/{id}", null, Body(widthCm: 1m, heightCm: 1m)))).StatusCode);

        var d = await DetailsAsync(c, id);
        Assert.Equal(60m, d.GetProperty("widthCm").GetDecimal());
        Assert.Equal("mirror", d.GetProperty("wallKind").GetString());
    }

    [Fact]
    public async Task Variants_only_link_visible_products_of_the_same_page()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var s1 = await SellerAsync(c, admin);
        var s2 = await SellerAsync(c, admin);
        var group = $"tv-{Guid.NewGuid():N}"[..20];
        var big = await CreateAsync(c, s1.Token, Body("شاشة 65", 750_000, 145.1m, 83m, 5.9m, "tv", group, "65 بوصة"));
        var small = await CreateAsync(c, s1.Token, Body("شاشة 43", 280_000, 96.3m, 56.1m, 7.6m, "tv", group, "43 بوصة"));
        var mid = await CreateAsync(c, s1.Token, Body("شاشة 55", 480_000, 123.1m, 70.9m, 5.9m, "tv", group, "55 بوصة"));
        // Same key on another page: never joins s1's group, and s1's products never show up there.
        var foreign = await CreateAsync(c, s2.Token, Body("شاشة غريبة", 100_000, 100m, 60m, null, "tv", group, "غريبة"));

        var d = await DetailsAsync(c, mid);
        Assert.Equal([small, mid, big], VariantIds(d)); // smallest first, the product itself included
        Assert.Equal("55 بوصة", d.GetProperty("variantLabel").GetString());
        var first = d.GetProperty("variants")[0];
        Assert.Equal("43 بوصة", first.GetProperty("label").GetString());
        Assert.Equal(280_000, first.GetProperty("price").GetInt64());
        Assert.Equal(4, first.GetProperty("stock").GetInt32());
        Assert.Equal(96.3m, first.GetProperty("widthCm").GetDecimal());
        Assert.Empty(VariantIds(await DetailsAsync(c, foreign)));

        // Switched off, hidden by admin or deleted siblings drop out.
        (await c.SendAsync(Req("PUT", $"/api/seller/v1/products/{big}", s1.Token, Body("شاشة 65", 750_000, 145.1m, 83m, 5.9m, "tv", group, "65 بوصة", active: false)))).EnsureSuccessStatusCode();
        Assert.Equal([small, mid], VariantIds(await DetailsAsync(c, mid)));
        (await c.SendAsync(Req("POST", $"/api/admin/v1/products/{small}/hide", admin))).EnsureSuccessStatusCode();
        Assert.Empty(VariantIds(await DetailsAsync(c, mid))); // only itself left
        (await c.SendAsync(Req("POST", $"/api/admin/v1/products/{small}/unhide", admin))).EnsureSuccessStatusCode();
        (await c.SendAsync(Req("DELETE", $"/api/seller/v1/products/{small}", s1.Token))).EnsureSuccessStatusCode();
        Assert.Empty(VariantIds(await DetailsAsync(c, mid)));
    }

    [Fact]
    public async Task A_variant_group_holds_at_most_ten_products_per_page()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var s1 = await SellerAsync(c, admin);
        var s2 = await SellerAsync(c, admin);
        var ids = new List<string>();
        for (var i = 1; i <= 10; i++)
            ids.Add(await CreateAsync(c, s1.Token, Body($"إطار {i}", 10_000 + i, 10m + i, 20m, null, "frame", "frames", $"{i}")));

        var r = await c.SendAsync(Req("POST", "/api/seller/v1/products", s1.Token, Body("إطار 11", 20_000, 30m, 40m, null, "frame", "frames", "11")));
        Assert.Equal("product.variants_full", (await r.ReadAsync()).GetProperty("code").GetString());
        // Editing a member of the full group still works, and the cap is per page.
        (await c.SendAsync(Req("PUT", $"/api/seller/v1/products/{ids[0]}", s1.Token, Body("إطار 1", 12_000, 11m, 20m, null, "frame", "frames", "1")))).EnsureSuccessStatusCode();
        await CreateAsync(c, s2.Token, Body("إطار", 10_000, 30m, 40m, null, "frame", "frames", "1"));
    }
}
