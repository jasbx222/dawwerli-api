using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace Dawwerli.Tests;

/// <summary>Courier link: token lifecycle, what the courier sees, location rules and who may read the live position.</summary>
[Collection("api")]
public class CourierTrackingTests(ApiFactory factory)
{
    private const int Clothes = 3;

    internal static HttpRequestMessage Req(string verb, string url, string? token, object? body = null)
    {
        var m = new HttpRequestMessage(new HttpMethod(verb), url);
        if (body is not null) m.Content = JsonContent.Create(body);
        if (token is not null) m.Headers.Authorization = new("Bearer", token);
        m.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return m;
    }

    private static async Task<JsonElement> OkAsync(HttpClient c, HttpRequestMessage m)
    {
        var r = await c.SendAsync(m);
        Assert.True(r.StatusCode == HttpStatusCode.OK, $"{m.Method} {m.RequestUri} -> {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
        return await r.ReadAsync();
    }

    internal sealed record Trip(string Customer, string CustomerPhone, string Page, string OrderId, string DealId, string CourierId, string Url, string Token);

    internal static async Task<Trip> TripAsync(HttpClient c, string admin, bool goOut = true)
    {
        var page = await Api.VerifiedPageAsync(c, admin, Clothes);
        var phone = "+964" + Api.NewPhone()[1..];
        var customer = await Api.LoginAsync(c, "customer", "0" + phone[4..]);
        (await c.SendAsync(Req("PUT", "/api/v1/me/profile", customer, new { name = "أحمد الزبون", governorateId = 1, area = "المنصور" }))).EnsureSuccessStatusCode();
        var req = await Api.RequestAsync(c, customer, Clothes, size: "M");
        var offer = (await (await Api.OfferAsync(c, page, req, 40000)).ReadAsync()).GetProperty("id").GetString()!;
        var sel = await OkAsync(c, Api.Post($"/api/v1/requests/{req}/select",
            new { offerId = offer, address = "بغداد - الكرادة داخل", latitude = 33.3, longitude = 44.42, locationNote = "قرب الجامع" }, customer));
        var id = sel.GetProperty("storeOrderId").GetString()!;
        var deal = sel.GetProperty("orderId").GetString()!;
        var courier = (await OkAsync(c, Req("POST", "/api/v1/pages/me/couriers", page, new { name = "علي المندوب", phone = "07801112233" }))).GetProperty("id").GetString()!;
        await OkAsync(c, Req("POST", $"/api/v1/pages/me/store-orders/{id}/status", page, new { status = "accepted" }));
        if (!goOut) return new Trip(customer, phone, page, id, deal, courier, "", "");
        var res = await OkAsync(c, Req("POST", $"/api/v1/pages/me/store-orders/{id}/status", page, new { status = "outForDelivery", courierId = courier }));
        var url = res.GetProperty("courierTrackingUrl").GetString()!;
        return new Trip(customer, phone, page, id, deal, courier, url, TokenOf(url));
    }

    private static string TokenOf(string url) => url[(url.LastIndexOf("/c/", StringComparison.Ordinal) + 3)..];

    private static Task<HttpResponseMessage> TrackAsync(HttpClient c, string token, string verb = "GET", string path = "", object? body = null) =>
        c.SendAsync(Req(verb, $"/api/v1/track/{token}{path}", null, body));

    [Fact]
    public async Task Going_out_with_a_courier_issues_a_link_that_shows_the_courier_the_order_and_the_customer_phone()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var t = await TripAsync(c, admin);
        Assert.Contains("/c/", t.Url);
        Assert.True(t.Token.Length >= 40);

        var view = await (await TrackAsync(c, t.Token)).ReadAsync();
        Assert.Equal("outForDelivery", view.GetProperty("status").GetString());
        Assert.Equal("أحمد الزبون", view.GetProperty("customerName").GetString());
        Assert.Equal(t.CustomerPhone, view.GetProperty("customerPhone").GetString());
        Assert.Equal("علي المندوب", view.GetProperty("courierName").GetString());
        Assert.Equal("بغداد - الكرادة داخل", view.GetProperty("address").GetString());
        Assert.Equal(33.3, view.GetProperty("location").GetProperty("latitude").GetDouble());
        Assert.Equal("قرب الجامع", view.GetProperty("location").GetProperty("note").GetString());
        Assert.Equal(40000, view.GetProperty("collectAmount").GetInt64());
        Assert.False(view.GetProperty("sharing").GetBoolean());

        // The page sees its link in the order details, never in list rows.
        var details = await OkAsync(c, Req("GET", $"/api/v1/pages/me/store-orders/{t.OrderId}", t.Page));
        Assert.Equal(t.Url, details.GetProperty("courierTrackingUrl").GetString());
        var list = await OkAsync(c, Req("GET", "/api/v1/pages/me/store-orders?status=active", t.Page));
        var row = list.GetProperty("items").EnumerateArray().Single(r => r.GetProperty("order").GetProperty("id").GetString() == t.OrderId);
        Assert.Equal(JsonValueKind.Null, row.GetProperty("courierTrackingUrl").ValueKind);

        // The courier page itself is served for the link.
        var html = await c.GetAsync($"/c/{t.Token}");
        Assert.Equal(HttpStatusCode.OK, html.StatusCode);
        Assert.Contains("ابدأ التوصيل", await html.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Customer_and_page_see_the_live_position_while_sharing_and_nobody_else_does()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var t = await TripAsync(c, admin);
        var mine = $"/api/v1/store/orders/{t.OrderId}/courier-location";

        // Not started: inactive, and locations are refused.
        Assert.False((await OkAsync(c, Req("GET", mine, t.Customer))).GetProperty("active").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await TrackAsync(c, t.Token, "POST", "/location", new { lat = 33.31, lng = 44.40 })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await TrackAsync(c, t.Token, "POST", "/start")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await TrackAsync(c, t.Token, "POST", "/location", new { lat = 33.31, lng = 44.40, accuracy = 12.5, heading = 90, speed = 8 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await TrackAsync(c, t.Token, "POST", "/location", new { lat = 33.305, lng = 44.41 })).StatusCode);

        var loc = await OkAsync(c, Req("GET", mine, t.Customer));
        Assert.True(loc.GetProperty("active").GetBoolean());
        Assert.Equal(33.305, loc.GetProperty("latitude").GetDouble());
        Assert.Equal(44.41, loc.GetProperty("longitude").GetDouble());
        Assert.True(loc.GetProperty("approximate").GetBoolean());
        Assert.InRange(loc.GetProperty("distanceMeters").GetDouble(), 500, 2000);
        Assert.True(loc.GetProperty("etaMinutes").GetInt32() >= 1);
        Assert.NotEqual(JsonValueKind.Null, loc.GetProperty("updatedAt").ValueKind);

        var pageLoc = await OkAsync(c, Req("GET", $"/api/v1/pages/me/store-orders/{t.OrderId}/courier-location", t.Page));
        Assert.True(pageLoc.GetProperty("active").GetBoolean());
        var details = await OkAsync(c, Req("GET", $"/api/v1/pages/me/store-orders/{t.OrderId}", t.Page));
        Assert.True(details.GetProperty("courierTracking").GetProperty("sharing").GetBoolean());

        // Another customer and another page get 404; an anonymous caller 401.
        var stranger = await Api.CustomerAsync(c);
        Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(Req("GET", mine, stranger))).StatusCode);
        var otherPage = await Api.VerifiedPageAsync(c, admin, Clothes);
        Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(Req("GET", $"/api/v1/pages/me/store-orders/{t.OrderId}/courier-location", otherPage))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Req("GET", mine, null))).StatusCode);

        // Stop clears the position.
        Assert.Equal(HttpStatusCode.OK, (await TrackAsync(c, t.Token, "POST", "/stop")).StatusCode);
        Assert.False((await OkAsync(c, Req("GET", mine, t.Customer))).GetProperty("active").GetBoolean());
    }

    [Theory]
    [InlineData(91, 44, null, null, null)]
    [InlineData(-91, 44, null, null, null)]
    [InlineData(33, 181, null, null, null)]
    [InlineData(33, -181, null, null, null)]
    [InlineData(33, 44, -1d, null, null)]
    [InlineData(33, 44, null, 361d, null)]
    [InlineData(33, 44, null, null, 150d)]
    public async Task Out_of_range_locations_are_refused(double lat, double lng, double? accuracy, double? heading, double? speed)
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var t = await TripAsync(c, admin);
        (await TrackAsync(c, t.Token, "POST", "/start")).EnsureSuccessStatusCode();
        var r = await TrackAsync(c, t.Token, "POST", "/location", new { lat, lng, accuracy, heading, speed });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Delivery_ends_the_link_and_wipes_the_position()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var t = await TripAsync(c, admin);
        (await TrackAsync(c, t.Token, "POST", "/start")).EnsureSuccessStatusCode();
        (await TrackAsync(c, t.Token, "POST", "/location", new { lat = 33.31, lng = 44.40 })).EnsureSuccessStatusCode();

        // Customer confirms at the door: store order becomes delivered.
        (await c.SendAsync(Api.Post($"/api/v1/orders/{t.DealId}/delivery-answer", new { answer = "received" }, t.Customer))).EnsureSuccessStatusCode();

        var gone = await TrackAsync(c, t.Token);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.DoesNotContain(t.CustomerPhone, await gone.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await TrackAsync(c, t.Token, "POST", "/location", new { lat = 33.31, lng = 44.40 })).StatusCode);
        Assert.False((await OkAsync(c, Req("GET", $"/api/v1/store/orders/{t.OrderId}/courier-location", t.Customer))).GetProperty("active").GetBoolean());
        var details = await OkAsync(c, Req("GET", $"/api/v1/pages/me/store-orders/{t.OrderId}", t.Page));
        Assert.Equal(JsonValueKind.Null, details.GetProperty("courierTrackingUrl").ValueKind);
    }

    [Fact]
    public async Task Page_delivered_or_cancel_ends_the_link()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var a = await TripAsync(c, admin);
        await OkAsync(c, Req("POST", $"/api/v1/pages/me/store-orders/{a.OrderId}/status", a.Page, new { status = "delivered" }));
        Assert.Equal(HttpStatusCode.NotFound, (await TrackAsync(c, a.Token)).StatusCode);

        var b = await TripAsync(c, admin);
        await OkAsync(c, Req("POST", $"/api/v1/pages/me/store-orders/{b.OrderId}/status", b.Page, new { status = "cancelled", reason = "الزبون ما يرد" }));
        var gone = await TrackAsync(c, b.Token);
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.DoesNotContain(b.CustomerPhone, await gone.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Reassigning_the_courier_or_regenerating_issues_a_new_link_and_kills_the_old_one()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var t = await TripAsync(c, admin);
        (await TrackAsync(c, t.Token, "POST", "/start")).EnsureSuccessStatusCode();
        (await TrackAsync(c, t.Token, "POST", "/location", new { lat = 33.31, lng = 44.40 })).EnsureSuccessStatusCode();

        var other = (await OkAsync(c, Req("POST", "/api/v1/pages/me/couriers", t.Page, new { name = "حسين", phone = "07809998877" }))).GetProperty("id").GetString()!;
        var moved = await OkAsync(c, Req("POST", $"/api/v1/pages/me/store-orders/{t.OrderId}/courier", t.Page, new { courierId = other }));
        Assert.Equal("حسين", moved.GetProperty("order").GetProperty("courier").GetProperty("name").GetString());
        var second = TokenOf(moved.GetProperty("courierTrackingUrl").GetString()!);
        Assert.NotEqual(t.Token, second);

        var old = await TrackAsync(c, t.Token);
        Assert.Equal(HttpStatusCode.NotFound, old.StatusCode);
        Assert.DoesNotContain(t.CustomerPhone, await old.Content.ReadAsStringAsync());
        Assert.False((await OkAsync(c, Req("GET", $"/api/v1/store/orders/{t.OrderId}/courier-location", t.Customer))).GetProperty("active").GetBoolean());
        var now = await (await TrackAsync(c, second)).ReadAsync();
        Assert.Equal("حسين", now.GetProperty("courierName").GetString());
        Assert.Equal(t.CustomerPhone, now.GetProperty("customerPhone").GetString());

        var regen = await OkAsync(c, Req("POST", $"/api/v1/pages/me/store-orders/{t.OrderId}/tracking-link", t.Page));
        var third = TokenOf(regen.GetProperty("url").GetString()!);
        Assert.Equal(HttpStatusCode.NotFound, (await TrackAsync(c, second)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await TrackAsync(c, third)).StatusCode);

        // Only the owning page can regenerate.
        var otherPage = await Api.VerifiedPageAsync(c, admin, Clothes);
        Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(Req("POST", $"/api/v1/pages/me/store-orders/{t.OrderId}/tracking-link", otherPage))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(Req("POST", $"/api/v1/pages/me/store-orders/{t.OrderId}/tracking-link", t.Customer))).StatusCode);
    }

    [Fact]
    public async Task No_link_before_the_order_goes_out_and_unknown_tokens_get_404()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var t = await TripAsync(c, admin, goOut: false);
        var details = await OkAsync(c, Req("GET", $"/api/v1/pages/me/store-orders/{t.OrderId}", t.Page));
        Assert.Equal(JsonValueKind.Null, details.GetProperty("courierTrackingUrl").ValueKind);
        Assert.Equal(422, (int)(await c.SendAsync(Req("POST", $"/api/v1/pages/me/store-orders/{t.OrderId}/tracking-link", t.Page))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await TrackAsync(c, new string('a', 43))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await TrackAsync(c, "short")).StatusCode);
    }
}

/// <summary>Same API with a tiny courier-link rate limit.</summary>
public sealed class TrackLimitApiFactory : ApiFactory
{
    protected override string DatabaseSuffix => ";Database=dawwerli_test_track";
    protected override void ConfigureWebHost(IWebHostBuilder b)
    {
        base.ConfigureWebHost(b);
        b.UseSetting("RateLimits:TrackPerMinute", "5");
    }
}

public class CourierLinkRateLimitTests(TrackLimitApiFactory factory) : IClassFixture<TrackLimitApiFactory>
{
    [Fact]
    public async Task Courier_link_calls_are_rate_limited_per_token()
    {
        var c = await factory.ClientAsync();
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 8; i++) codes.Add((await c.GetAsync($"/api/v1/track/{new string('b', 43)}")).StatusCode);
        Assert.Contains(HttpStatusCode.TooManyRequests, codes);
        Assert.Equal(5, codes.Count(x => x == HttpStatusCode.NotFound));
        // A different token has its own budget.
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/v1/track/{new string('c', 43)}")).StatusCode);
    }
}
