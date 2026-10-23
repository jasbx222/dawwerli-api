using System.Net;
using System.Text.Json;
using Dawwerli.Application.Contracts;
using Dawwerli.Domain.Entities;
using Dawwerli.Domain.Rules;
using Dawwerli.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SearchRules = Dawwerli.Application.Services.PageProductSearch;
using SearchMigration = Dawwerli.Infrastructure.Persistence.Migrations.PageProductSearch;

namespace Dawwerli.Tests;

public class ArabicTextTests
{
    [Theory]
    [InlineData("أحمر شفاه", "احمر شفاه")]
    [InlineData("إطار صور", "اطار صور")]
    [InlineData("آيفون 15", "ايفون 15")]
    [InlineData("ٱلكتاب", "الكتاب")]
    [InlineData("مراية دائرية", "مرايه دايريه")]
    [InlineData("ثلاجة", "ثلاجه")]
    [InlineData("مستشفى", "مستشفي")]
    [InlineData("مؤقت", "موقت")]
    [InlineData("گيمنك", "كيمنك")]
    [InlineData("ک", "ك")]
    [InlineData("ی", "ي")]
    [InlineData("شَاشَةٌ", "شاشه")]
    [InlineData("مـــرحبا", "مرحبا")]
    [InlineData("سامسونج ٢٤", "سامسونج 24")]
    [InlineData("۱۲۳", "123")]
    [InlineData("  iPhone   PRO\t Max ", "iphone pro max")]
    [InlineData("Air\u00A0Pods", "air pods")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void Folds_letter_forms_diacritics_digits_case_and_spaces(string input, string expected) =>
        Assert.Equal(expected, ArabicText.Normalize(input));

    [Fact]
    public void Never_grows_the_text() =>
        Assert.All(new[] { "شَاشَةٌ سمارت ٤K", "  a  b  ", "ﻻ", "😀 كوب" }, s => Assert.True(ArabicText.Normalize(s).Length <= s.Length));

    [Fact]
    public void Product_keeps_its_search_key_in_step_with_the_name()
    {
        var p = new Product { Name = "شاشة سمارت 4K" };
        Assert.Equal("شاشه سمارت 4k", p.NormalizedName);
        p.Name = "أحمر شفاه";
        Assert.Equal("احمر شفاه", p.NormalizedName);
    }
}

public class PageProductQueryValidationTests
{
    private static string? Code(PageProductQuery q) => SearchRules.Parse(q).Error?.Code;

    [Fact]
    public void Accepts_defaults_and_folds_the_term()
    {
        var r = SearchRules.Parse(new PageProductQuery());
        Assert.Null(r.Error);
        Assert.Null(r.Value!.Term);
        Assert.Empty(r.Value.CategoryIds);
        Assert.Equal(Application.Services.PageProductSort.Newest, r.Value.Sort);
        Assert.Equal("شاشه", SearchRules.Parse(new PageProductQuery(Q: "  شاشة ")).Value!.Term);
        Assert.Null(SearchRules.Parse(new PageProductQuery(Q: "ـــ")).Value!.Term); // only tatweel: no filter
    }

    [Fact]
    public void Parses_repeated_and_comma_category_lists_without_duplicates()
    {
        var r = SearchRules.Parse(new PageProductQuery(CategoryIds: ["1,8", " 8 ", "11", ""]));
        Assert.Equal([1, 8, 11], r.Value!.CategoryIds);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-3")]
    [InlineData("abc")]
    [InlineData("1;DROP TABLE products")]
    [InlineData("99999999999")]
    public void Rejects_bad_category_ids(string id) => Assert.Equal("products.category_invalid", Code(new PageProductQuery(CategoryIds: [id])));

    [Fact]
    public void Rejects_out_of_range_inputs()
    {
        Assert.Equal("products.q_too_long", Code(new PageProductQuery(Q: new string('ا', 61))));
        Assert.Null(Code(new PageProductQuery(Q: new string('ا', 60))));
        Assert.Equal("products.too_many_categories", Code(new PageProductQuery(CategoryIds: [string.Join(',', Enumerable.Range(1, 21))])));
        Assert.Null(Code(new PageProductQuery(CategoryIds: [string.Join(',', Enumerable.Range(1, 20))])));
        Assert.Equal("products.price_invalid", Code(new PageProductQuery(MinPrice: -1)));
        Assert.Equal("products.price_invalid", Code(new PageProductQuery(MaxPrice: SearchRules.MaxPrice + 1)));
        Assert.Equal("products.price_range", Code(new PageProductQuery(MinPrice: 5000, MaxPrice: 4000)));
        Assert.Null(Code(new PageProductQuery(MinPrice: 5000, MaxPrice: 5000)));
        Assert.Equal("products.sort_invalid", Code(new PageProductQuery(Sort: "name; --")));
        Assert.Null(Code(new PageProductQuery(Sort: "PRICE_ASC")));
    }

    [Fact]
    public void Escapes_like_wildcards() => Assert.Equal(@"50\% \_x\\", SearchRules.EscapeLike(@"50% _x\"));
}

[Collection("api")]
public class PageProductSearchTests(ApiFactory factory)
{
    private static int Phones, Electronics, Gaming, Other;

    /// <summary>Catalog ids come from the seed order, so look them up by slug.</summary>
    private async Task CategoriesAsync()
    {
        if (Phones != 0) return;
        await using var scope = factory.Services.CreateAsyncScope();
        var ids = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Categories.ToDictionaryAsync(x => x.Slug, x => x.Id);
        (Electronics, Gaming, Other) = (ids["electronics"], ids["gaming"], ids["flowers"]);
        Phones = ids["phones"];
    }

    private sealed record Seeded(string PageId, Dictionary<string, Guid> Ids);

    /// <summary>A fresh verified page with products written straight to the database (so ratings and sales can be set).</summary>
    private async Task<Seeded> PageAsync(HttpClient c)
    {
        await CategoriesAsync();
        var admin = await Api.AdminAsync(c, factory);
        var token = await Api.VerifiedPageAsync(c, admin, Phones);
        var pageId = (await (await c.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/v1/pages/me")
            { Headers = { Authorization = new("Bearer", token) } })).ReadAsync()).GetProperty("id").GetString()!;
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var page = await db.Pages.SingleAsync(p => p.Id == Guid.Parse(pageId));
        var t0 = DateTime.UtcNow.AddDays(-10);
        Product P(string name, long price, int cat, int stock, int day, double rating = 0, int ratingCount = 0) => new()
        {
            Id = Guid.NewGuid(), PageId = page.Id, Name = name, Price = price, CategoryId = cat, Stock = stock, Active = true,
            ImageKeys = ["products/a.webp"], Rating = rating, RatingCount = ratingCount, CreatedAt = t0.AddDays(day),
        };
        var rows = new[]
        {
            P("آيفون 15 برو ماكس", 1_650_000, Phones, 4, 1, 4.9, 40),
            P("كفر سيليكون مع حماية كاميرا", 10_000, Phones, 30, 2, 4.1, 12),
            P("شاحن سريع 25 واط أصلي", 20_000, Phones, 0, 3, 4.6, 9),
            P("ايربودز برو الجيل الثاني", 180_000, Electronics, 6, 4, 4.6, 30),
            P("شاشة سمارت 4K 55 بوصة", 520_000, Electronics, 2, 5, 4.4, 8),
            P("بلايستيشن 5 سلم", 820_000, Gaming, 1, 6, 4.8, 20),
            P("خصم 100% _ حصري", 5_000, Phones, 3, 7),
        };
        db.Products.AddRange(rows);
        var hidden = P("شاشة مخفية", 1, Electronics, 5, 8);
        hidden.HiddenByAdmin = true;
        var inactive = P("شاشة متوقفة", 1, Electronics, 5, 8);
        inactive.Active = false;
        var deleted = P("شاشة محذوفة", 1, Electronics, 5, 8);
        deleted.DeletedAt = DateTime.UtcNow;
        db.Products.AddRange(hidden, inactive, deleted);
        // Sales: the cover outsells everything, a cancelled order never counts.
        void Sold(Product p, int qty, StoreOrderStatus status) => db.StoreOrders.Add(new StoreOrder
        {
            Id = Guid.NewGuid(), Number = Random.Shared.NextInt64(9_000_000_000, 9_900_000_000), CustomerUserId = page.UserId, PageId = page.Id,
            Status = status, Address = "بغداد", StatusChangedAt = DateTime.UtcNow,
            Items = [new StoreOrderItem { ProductId = p.Id, Name = p.Name, UnitPrice = p.Price, Quantity = qty }],
        });
        Sold(rows[1], 9, StoreOrderStatus.Delivered);
        Sold(rows[3], 3, StoreOrderStatus.Accepted);
        Sold(rows[0], 50, StoreOrderStatus.Cancelled);
        await db.SaveChangesAsync();
        return new Seeded(pageId, rows.ToDictionary(r => r.Name, r => r.Id));
    }

    private static async Task<JsonElement> GetAsync(HttpClient c, string url)
    {
        var r = await c.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await r.ReadAsync();
    }

    private static List<string> Names(JsonElement page) => page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()!).ToList();

    [Fact]
    public async Task Search_folds_arabic_forms_and_only_lists_visible_products()
    {
        var c = await factory.ClientAsync();
        var s = await PageAsync(c);
        var url = $"/api/v1/pages/{s.PageId}/products";

        var all = await GetAsync(c, url);
        Assert.Equal(7, all.GetProperty("total").GetInt32());
        Assert.DoesNotContain(Names(all), n => n.Contains("مخفية") || n.Contains("متوقفة") || n.Contains("محذوفة"));

        Assert.Equal(["شاشة سمارت 4K 55 بوصة"], Names(await GetAsync(c, $"{url}?q={Uri.EscapeDataString("شاشه")}")));
        Assert.Equal(["شاشة سمارت 4K 55 بوصة"], Names(await GetAsync(c, $"{url}?q={Uri.EscapeDataString("شَاشَة سمارت ٤k")}")));
        Assert.Equal(["آيفون 15 برو ماكس"], Names(await GetAsync(c, $"{url}?q={Uri.EscapeDataString("ايفون")}")));
        Assert.Equal(["آيفون 15 برو ماكس"], Names(await GetAsync(c, $"{url}?q={Uri.EscapeDataString("أيفون ١٥")}")));
        Assert.Equal(["ايربودز برو الجيل الثاني"], Names(await GetAsync(c, $"{url}?q={Uri.EscapeDataString("إيربودز")}")));
        // Wildcards are literal: "%" only finds the product that has one.
        Assert.Equal(["خصم 100% _ حصري"], Names(await GetAsync(c, $"{url}?q={Uri.EscapeDataString("%")}")));
        Assert.Equal(["خصم 100% _ حصري"], Names(await GetAsync(c, $"{url}?q=_")));
        Assert.Equal(0, (await GetAsync(c, $"{url}?q={Uri.EscapeDataString("' OR 1=1 --")}")).GetProperty("total").GetInt32());
        Assert.Equal(7, (await GetAsync(c, $"{url}?q=%20%20")).GetProperty("total").GetInt32());

        var card = (await GetAsync(c, $"{url}?q={Uri.EscapeDataString("بلايستيشن")}")).GetProperty("items")[0];
        Assert.Equal(Gaming, card.GetProperty("categoryId").GetInt32());
        Assert.False(string.IsNullOrEmpty(card.GetProperty("categoryName").GetString()));
    }

    [Fact]
    public async Task Filters_combine_categories_price_and_stock()
    {
        var c = await factory.ClientAsync();
        var s = await PageAsync(c);
        var url = $"/api/v1/pages/{s.PageId}/products";

        Assert.Equal(2, (await GetAsync(c, $"{url}?categoryIds={Electronics}")).GetProperty("total").GetInt32());
        Assert.Equal(3, (await GetAsync(c, $"{url}?categoryIds={Electronics}&categoryIds={Gaming}")).GetProperty("total").GetInt32());
        Assert.Equal(3, (await GetAsync(c, $"{url}?categoryIds={Electronics},{Gaming}")).GetProperty("total").GetInt32());
        Assert.Equal(["كفر سيليكون مع حماية كاميرا", "شاحن سريع 25 واط أصلي"],
            Names(await GetAsync(c, $"{url}?categoryIds={Phones}&minPrice=10000&maxPrice=20000&sort=price_asc")));
        Assert.Equal(["كفر سيليكون مع حماية كاميرا"], Names(await GetAsync(c, $"{url}?categoryIds={Phones}&minPrice=10000&maxPrice=20000&inStock=true")));
        Assert.Equal(6, (await GetAsync(c, $"{url}?inStock=true")).GetProperty("total").GetInt32());
        Assert.Equal(7, (await GetAsync(c, $"{url}?inStock=false")).GetProperty("total").GetInt32());
        var combined = await GetAsync(c, $"{url}?q={Uri.EscapeDataString("برو")}&categoryIds={Electronics}&maxPrice=200000&pageSize=1");
        Assert.Equal(1, combined.GetProperty("total").GetInt32());
        Assert.Equal(1, combined.GetProperty("pageSize").GetInt32());
        // A category the page has nothing in: empty, not an error.
        Assert.Equal(0, (await GetAsync(c, $"{url}?categoryIds={Other}")).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Sorts_are_stable_and_paged()
    {
        var c = await factory.ClientAsync();
        var s = await PageAsync(c);
        var url = $"/api/v1/pages/{s.PageId}/products";

        var newest = Names(await GetAsync(c, url));
        Assert.Equal("خصم 100% _ حصري", newest[0]);
        Assert.Equal("شاحن سريع 25 واط أصلي", newest[^1]); // out of stock goes last
        Assert.Equal("خصم 100% _ حصري", Names(await GetAsync(c, $"{url}?sort=price_asc"))[0]);
        Assert.Equal("آيفون 15 برو ماكس", Names(await GetAsync(c, $"{url}?sort=price_desc"))[0]);
        Assert.Equal(["آيفون 15 برو ماكس", "بلايستيشن 5 سلم"], Names(await GetAsync(c, $"{url}?sort=top_rated"))[..2]);
        // Delivered and accepted orders count, the cancelled 50 iPhones do not.
        var best = Names(await GetAsync(c, $"{url}?sort=best_selling"));
        Assert.Equal(["كفر سيليكون مع حماية كاميرا", "ايربودز برو الجيل الثاني", "آيفون 15 برو ماكس"], best[..3]);

        var paged = new List<string>();
        for (var p = 1; p <= 4; p++) paged.AddRange(Names(await GetAsync(c, $"{url}?sort=price_desc&pageSize=2&page={p}")));
        Assert.Equal(Names(await GetAsync(c, $"{url}?sort=price_desc")), paged);
        var beyond = await GetAsync(c, $"{url}?page=99&pageSize=2");
        Assert.Empty(beyond.GetProperty("items").EnumerateArray());
        Assert.Equal(7, beyond.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Facets_count_visible_products_per_category()
    {
        var c = await factory.ClientAsync();
        var s = await PageAsync(c);
        var f = await GetAsync(c, $"/api/v1/pages/{s.PageId}/product-facets");
        Assert.Equal(7, f.GetProperty("total").GetInt32());
        Assert.Equal(6, f.GetProperty("inStock").GetInt32());
        Assert.Equal(5_000, f.GetProperty("minPrice").GetInt64());
        Assert.Equal(1_650_000, f.GetProperty("maxPrice").GetInt64());
        var cats = f.GetProperty("categories").EnumerateArray()
            .Select(x => (x.GetProperty("id").GetInt32(), x.GetProperty("slug").GetString(), x.GetProperty("count").GetInt32())).ToList();
        Assert.Equal([(Phones, "phones", 4), (Electronics, "electronics", 2), (Gaming, "gaming", 1)], cats);
        Assert.All(f.GetProperty("categories").EnumerateArray(), x => Assert.False(string.IsNullOrEmpty(x.GetProperty("name").GetString())));
    }

    [Fact]
    public async Task Empty_page_facets_and_unknown_or_unverified_pages()
    {
        var c = await factory.ClientAsync();
        await CategoriesAsync();
        var admin = await Api.AdminAsync(c, factory);
        var token = await Api.VerifiedPageAsync(c, admin, Phones);
        var pageId = (await (await c.SendAsync(new HttpRequestMessage(HttpMethod.Get, "/api/v1/pages/me")
            { Headers = { Authorization = new("Bearer", token) } })).ReadAsync()).GetProperty("id").GetString()!;
        var empty = await GetAsync(c, $"/api/v1/pages/{pageId}/product-facets");
        Assert.Equal(0, empty.GetProperty("total").GetInt32());
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("minPrice").ValueKind);
        Assert.Empty(empty.GetProperty("categories").EnumerateArray());

        var unknown = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/v1/pages/{unknown}/products?q=x")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/v1/pages/{unknown}/product-facets")).StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Pages.Where(p => p.Id == Guid.Parse(pageId)).ExecuteUpdateAsync(x => x.SetProperty(p => p.SuspendedUntil, DateTime.UtcNow.AddDays(1)));
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/v1/pages/{pageId}/products")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/v1/pages/{pageId}/product-facets")).StatusCode);
    }

    [Theory]
    [InlineData("sort=cheapest")]
    [InlineData("minPrice=-5")]
    [InlineData("minPrice=10&maxPrice=5")]
    [InlineData("categoryIds=abc")]
    [InlineData("categoryIds=0")]
    [InlineData("inStock=maybe")]
    [InlineData("minPrice=1e9")]
    public async Task Invalid_inputs_are_400(string query)
    {
        var c = await factory.ClientAsync();
        var s = await PageAsync(c);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync($"/api/v1/pages/{s.PageId}/products?{query}")).StatusCode);
    }

    [Fact]
    public async Task Long_search_and_category_lists_are_400()
    {
        var c = await factory.ClientAsync();
        var s = await PageAsync(c);
        var url = $"/api/v1/pages/{s.PageId}/products";
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync($"{url}?q={new string('a', 61)}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.GetAsync($"{url}?categoryIds={string.Join(',', Enumerable.Range(1, 21))}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync($"{url}?q={new string('a', 60)}")).StatusCode);
    }

    /// <summary>The seller portal path (and any later rename) keeps normalized_name in step; the migration's SQL folds like C#.</summary>
    [Fact]
    public async Task Saved_names_and_the_migration_backfill_fold_like_the_domain_rule()
    {
        await factory.ClientAsync();
        await CategoriesAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var samples = new[]
        {
            "شَاشَةٌ سمارت ٤K", "أحمر شفاه مات", "إطار صور خشب منقوش", "آيفون 15 برو ماكس", "ٱلمنتج", "مراية دائرية بإطار ذهبي",
            "مستشفى", "مؤقت", "گيمنك ک ی", "مـــرحبا", "۱۲۳ ٤٥٦", "  iPhone   PRO\t Max ", "Air\u00A0Pods", "it's 50% _off_ \\", "", "   ", "😀 كوب",
            // Every mapped and removed character, so the migration's frozen copy cannot drift from the domain rule.
            "x" + ArabicText.From + "y" + ArabicText.Removed + "z", string.Concat(ArabicText.From.Select(ch => $"{ch}a")),
        };
        foreach (var sample in samples)
        {
            // The rule's regex quantifier has braces, which SqlQueryRaw would read as format items.
            var expr = SearchMigration.NormalizeSql("@@x").Replace("{", "{{").Replace("}", "}}").Replace("@@x", "{0}");
            var sql = $"SELECT {expr} AS \"Value\"";
            var folded = await db.Database.SqlQueryRaw<string>(sql, sample).SingleAsync();
            Assert.Equal(ArabicText.Normalize(sample), folded);
        }

        var page = await db.Pages.FirstAsync();
        var p = new Product { Id = Guid.NewGuid(), PageId = page.Id, Name = "شاشة سمارت", Price = 1000, CategoryId = Electronics, ImageKeys = [] };
        db.Products.Add(p);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var tracked = await db.Products.SingleAsync(x => x.Id == p.Id);
        Assert.Equal("شاشه سمارت", tracked.NormalizedName);
        tracked.Name = "مراية ذهبية";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.Equal("مرايه ذهبيه", await db.Products.Where(x => x.Id == p.Id).Select(x => x.NormalizedName).SingleAsync());
        await db.Products.Where(x => x.Id == p.Id).ExecuteDeleteAsync();
    }
}
