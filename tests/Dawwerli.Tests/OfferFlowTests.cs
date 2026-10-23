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
    public async Task Published_requests_are_open_to_every_page_right_away()
    {
        // The early-access window was retired: a request is dispatched to all pages on publish.
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var early = new List<string>();
        for (var i = 0; i < 3; i++) early.Add(await Api.VerifiedPageAsync(c, admin, Perfumes, earlyAccess: true));
        var normal = await Api.VerifiedPageAsync(c, admin, Perfumes);
        var customer = await Api.CustomerAsync(c);
        var req = await Api.RequestAsync(c, customer, Perfumes);

        foreach (var page in early) Assert.Equal(HttpStatusCode.OK, (await Api.OfferAsync(c, page, req)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Api.OfferAsync(c, normal, req)).StatusCode);
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

    private static HttpRequestMessage Get(string url, string token)
    {
        var m = new HttpRequestMessage(HttpMethod.Get, url);
        m.Headers.Authorization = new("Bearer", token);
        return m;
    }

    private static async Task<string> UploadAsync(HttpClient c, string token, string fileName, byte[] bytes)
    {
        var form = new MultipartFormDataContent { { new ByteArrayContent(bytes), "file", fileName }, { new StringContent("requests"), "kind" } };
        var m = new HttpRequestMessage(HttpMethod.Post, "/api/v1/media") { Content = form };
        m.Headers.Authorization = new("Bearer", token);
        var r = await c.SendAsync(m);
        r.EnsureSuccessStatusCode();
        return (await r.ReadAsync()).GetProperty("key").GetString()!;
    }

    /// <summary>The customer app's post flow: upload, then POST /requests with its own condition codes (brandNew is the default).</summary>
    [Theory]
    [InlineData("photo", "brandNew")]
    [InlineData("photo", "used")]
    [InlineData("text", "any")]
    [InlineData("voice", "brandNew")]
    public async Task App_post_request_body_is_accepted(string mode, string condition)
    {
        var c = await factory.ClientAsync();
        var customer = await Api.CustomerAsync(c);
        string? key = mode switch
        {
            "photo" => await UploadAsync(c, customer, "image_picker_1.jpg", [0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0]),
            "voice" => await UploadAsync(c, customer, "voice_1.m4a", [0, 0, 0, 0x20, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'M', (byte)'4', (byte)'A']),
            _ => null,
        };
        // Exact body of ApiPostRepository.publish: nulls are sent, not omitted.
        var r = await c.SendAsync(Api.Post("/api/v1/requests", new
        {
            mode, mediaKey = key, description = "اريد كريم ترطيب للوجه نوع جيد للبشرة الجافة ويكون اصلي ومختوم",
            voiceSeconds = mode == "voice" ? 12 : (int?)null, categoryId = Care,
            size = (string?)null, color = (string?)null, condition, maxPrice = 25000, governorateId = 1, readyToBuy = true,
        }, customer));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var id = (await r.ReadAsync()).GetProperty("id").GetString();
        var mine = await (await c.SendAsync(Get("/api/v1/requests/mine", customer))).ReadAsync();
        Assert.Contains(mine.EnumerateArray(), x => x.GetProperty("id").GetString() == id);
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
