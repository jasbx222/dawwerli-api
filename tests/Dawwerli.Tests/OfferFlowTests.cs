using System.Net;

namespace Dawwerli.Tests;

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>;

[Collection("api")]
public class OfferFlowTests(ApiFactory factory)
{
    private const int Clothes = 3, Perfumes = 4, Watches = 5, Care = 6;

    [Fact]
    public async Task Sixth_and_seventh_concurrent_offers_are_rejected()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var pages = new List<string>();
        for (var i = 0; i < 7; i++) pages.Add(await Api.VerifiedPageAsync(c, admin, Clothes));
        var customer = await Api.CustomerAsync(c);
        var req = await Api.RequestAsync(c, customer, Clothes, size: "M");

        var results = await Task.WhenAll(pages.Select(p => Api.OfferAsync(c, p, req)));

        Assert.Equal(5, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.All(results.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
    }

    [Fact]
    public async Task Early_access_pages_are_capped_at_two_slots_during_the_window()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var early = new List<string>();
        for (var i = 0; i < 3; i++) early.Add(await Api.VerifiedPageAsync(c, admin, Perfumes, earlyAccess: true));
        var normal = await Api.VerifiedPageAsync(c, admin, Perfumes);
        var customer = await Api.CustomerAsync(c);
        var req = await Api.RequestAsync(c, customer, Perfumes);

        Assert.Equal(HttpStatusCode.OK, (await Api.OfferAsync(c, early[0], req)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Api.OfferAsync(c, early[1], req)).StatusCode);
        var third = await Api.OfferAsync(c, early[2], req);
        Assert.Equal(HttpStatusCode.Conflict, third.StatusCode);
        Assert.Equal("offer.early_full", (await third.ReadAsync()).GetProperty("code").GetString());
        var tooSoon = await Api.OfferAsync(c, normal, req);
        Assert.Equal(HttpStatusCode.Forbidden, tooSoon.StatusCode); // not dispatched to normal pages yet
    }

    [Fact]
    public async Task Selection_reveals_phones_once_and_rejects_a_second_selection()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var p1 = await Api.VerifiedPageAsync(c, admin, Watches);
        var p2 = await Api.VerifiedPageAsync(c, admin, Watches);
        var customer = await Api.CustomerAsync(c);
        var req = await Api.RequestAsync(c, customer, Watches);
        var o1 = (await (await Api.OfferAsync(c, p1, req, 40000)).ReadAsync()).GetProperty("id").GetString();
        var o2 = (await (await Api.OfferAsync(c, p2, req, 45000)).ReadAsync()).GetProperty("id").GetString();

        var sel = await c.SendAsync(Api.Post($"/api/v1/requests/{req}/select", new { offerId = o1 }, customer));
        Assert.Equal(HttpStatusCode.OK, sel.StatusCode);
        Assert.False(string.IsNullOrEmpty((await sel.ReadAsync()).GetProperty("pagePhone").GetString()));
        var again = await c.SendAsync(Api.Post($"/api/v1/requests/{req}/select", new { offerId = o2 }, customer));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Idempotency_key_returns_the_same_request()
    {
        var c = await factory.ClientAsync();
        var customer = await Api.CustomerAsync(c);
        var body = new { mode = "photo", mediaKey = "requests/x.webp", description = "مرة وحدة", categoryId = Care, condition = "new", maxPrice = 10000, governorateId = 1, readyToBuy = true };
        var m1 = Api.Post("/api/v1/requests", body, customer);
        var m2 = Api.Post("/api/v1/requests", body, customer);
        m2.Headers.Remove("Idempotency-Key");
        m2.Headers.Add("Idempotency-Key", m1.Headers.GetValues("Idempotency-Key").First());
        var a = (await (await c.SendAsync(m1)).ReadAsync()).GetProperty("id").GetString();
        var b = (await (await c.SendAsync(m2)).ReadAsync()).GetProperty("id").GetString();
        Assert.Equal(a, b);
    }

    [Fact]
    public async Task Rejects_request_with_contact_info_and_banned_item()
    {
        var c = await factory.ClientAsync();
        var customer = await Api.CustomerAsync(c);
        var r = await c.SendAsync(Api.Post("/api/v1/requests", new
        {
            mode = "text", description = "أريد مسدس كلمني 07701234567", categoryId = Care, condition = "new", maxPrice = 1000, governorateId = 1, readyToBuy = true,
        }, customer));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        var errors = (await r.ReadAsync()).GetProperty("errors").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("contactInfo", errors);
        Assert.Contains("prohibited", errors);
    }

    [Fact]
    public async Task Five_wrong_codes_lock_the_phone()
    {
        var c = await factory.ClientAsync();
        var phone = Api.NewPhone();
        await c.SendAsync(Api.Post("/api/v1/auth/otp/request", new { phone }, null));
        HttpResponseMessage last = null!;
        for (var i = 0; i < 5; i++) last = await c.SendAsync(Api.Post("/api/v1/auth/otp/verify", new { phone, code = "000000x".Replace("x", ""), role = "customer" }, null));
        Assert.Equal(HttpStatusCode.TooManyRequests, last.StatusCode);
        Assert.Equal("otp.locked", (await last.ReadAsync()).GetProperty("code").GetString());
    }
}
