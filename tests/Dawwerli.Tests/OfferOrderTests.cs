using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dawwerli.Domain.Rules;

namespace Dawwerli.Tests;

/// <summary>Chosen offers run through the store-order lifecycle; couriers, delivery pins, anti-leak and who may see the customer phone.</summary>
[Collection("api")]
public class OfferOrderTests(ApiFactory factory)
{
    private const int Clothes = 3, Watches = 5;

    private static HttpRequestMessage Req(string verb, string url, string? token, object? body = null)
    {
        var m = new HttpRequestMessage(new HttpMethod(verb), url);
        if (body is not null) m.Content = JsonContent.Create(body);
        if (token is not null) m.Headers.Authorization = new("Bearer", token);
        m.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return m;
    }

    private static async Task<JsonElement> GetAsync(HttpClient c, string url, string token)
    {
        var r = await c.SendAsync(Req("GET", url, token));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return await r.ReadAsync();
    }

    private static async Task<string> RawAsync(HttpClient c, string verb, string url, string token, object? body = null)
    {
        var r = await c.SendAsync(Req(verb, url, token, body));
        Assert.True(r.IsSuccessStatusCode, $"{verb} {url} -> {(int)r.StatusCode}");
        return await r.Content.ReadAsStringAsync();
    }

    private static async Task<JsonElement> MoveAsync(HttpClient c, string page, string id, string status, string? courierId = null)
    {
        var r = await c.SendAsync(Req("POST", $"/api/v1/pages/me/store-orders/{id}/status", page, new { status, courierId }));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await r.ReadAsync()).GetProperty("order");
    }

    private static async Task<(string Token, string Phone)> NamedCustomerAsync(HttpClient c)
    {
        var phone = Api.NewPhone();
        var t = await Api.LoginAsync(c, "customer", phone);
        (await c.SendAsync(Req("PUT", "/api/v1/me/profile", t, new { name = "زبون", governorateId = 1, area = "المنصور" }))).EnsureSuccessStatusCode();
        return (t, phone);
    }

    private sealed record Deal(string Customer, string CustomerPhone, string Page, string Request, string Offer, string StoreOrderId, string OrderId);

    private async Task<Deal> DealAsync(HttpClient c, string admin, int category, object? pin = null)
    {
        var page = await Api.VerifiedPageAsync(c, admin, category);
        var (customer, phone) = await NamedCustomerAsync(c);
        var req = await Api.RequestAsync(c, customer, category, size: "M");
        var offer = (await (await Api.OfferAsync(c, page, req, 40000)).ReadAsync()).GetProperty("id").GetString()!;
        var sel = await c.SendAsync(Api.Post($"/api/v1/requests/{req}/select", pin ?? new { offerId = offer }, customer));
        Assert.Equal(HttpStatusCode.OK, sel.StatusCode);
        var body = await sel.ReadAsync();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("whatsAppLink").ValueKind);
        return new Deal(customer, phone, page, req, offer, body.GetProperty("storeOrderId").GetString()!, body.GetProperty("orderId").GetString()!);
    }

    private static async Task<string> CourierAsync(HttpClient c, string page, string name = "علي المندوب", string phone = "07801112233")
    {
        var r = await c.SendAsync(Req("POST", "/api/v1/pages/me/couriers", page, new { name, phone }));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await r.ReadAsync()).GetProperty("id").GetString()!;
    }

    // ---------- lifecycle ----------

    [Fact]
    public async Task Chosen_offer_becomes_a_page_order_and_runs_the_store_lifecycle_with_inspect_before_pay()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var page = await Api.VerifiedPageAsync(c, admin, Clothes);
        var (customer, _) = await NamedCustomerAsync(c);
        var req = await Api.RequestAsync(c, customer, Clothes, size: "M");
        var offer = (await (await Api.OfferAsync(c, page, req, 40000)).ReadAsync()).GetProperty("id").GetString()!;
        var sel = await (await c.SendAsync(Api.Post($"/api/v1/requests/{req}/select",
            new { offerId = offer, address = "بغداد - الكرادة", latitude = 33.3, longitude = 44.42, locationNote = "قرب الجامع" }, customer))).ReadAsync();
        var id = sel.GetProperty("storeOrderId").GetString()!;
        var orderId = sel.GetProperty("orderId").GetString()!;

        // The page sees it in its normal orders list; the customer in theirs.
        var list = await GetAsync(c, "/api/v1/pages/me/store-orders?status=new", page);
        var row = list.GetProperty("items").EnumerateArray().Single(r => r.GetProperty("order").GetProperty("id").GetString() == id).GetProperty("order");
        Assert.Equal("offer", row.GetProperty("kind").GetString());
        Assert.Equal(40000, row.GetProperty("subtotal").GetInt64());
        Assert.Equal(33.3, row.GetProperty("location").GetProperty("latitude").GetDouble());
        Assert.Equal("قرب الجامع", row.GetProperty("location").GetProperty("note").GetString());
        var mine = await GetAsync(c, "/api/v1/store/orders", customer);
        Assert.Contains(mine.GetProperty("items").EnumerateArray(), o => o.GetProperty("id").GetString() == id);

        // Answering before the order goes out is refused.
        var early = await c.SendAsync(Api.Post($"/api/v1/orders/{orderId}/delivery-answer", new { answer = "received" }, customer));
        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);

        var accepted = await MoveAsync(c, page, id, "accepted");
        Assert.Equal("awaitingDelivery", accepted.GetProperty("dealStatus").GetString());
        var courier = await CourierAsync(c, page);
        var outFor = await MoveAsync(c, page, id, "outForDelivery", courier);
        Assert.Equal("علي المندوب", outFor.GetProperty("courier").GetProperty("name").GetString());

        // Customer sees the courier and may inspect now.
        var view = await GetAsync(c, $"/api/v1/store/orders/{id}", customer);
        Assert.Equal("07801112233", view.GetProperty("courier").GetProperty("phone").GetString());
        Assert.True(view.GetProperty("canAnswer").GetBoolean());
        var notes = await GetAsync(c, "/api/v1/notifications", customer);
        Assert.Contains("علي المندوب", notes.GetRawText());

        var ok = await c.SendAsync(Api.Post($"/api/v1/orders/{orderId}/delivery-answer", new { answer = "received" }, customer));
        Assert.True(ok.IsSuccessStatusCode);
        var done = await GetAsync(c, $"/api/v1/store/orders/{id}", customer);
        Assert.Equal("delivered", done.GetProperty("status").GetString());
        Assert.Equal("received", done.GetProperty("answer").GetString());
        Assert.True(done.GetProperty("canRate").GetBoolean());
        Assert.Equal(["new", "accepted", "outForDelivery", "delivered"],
            done.GetProperty("history").EnumerateArray().Select(h => h.GetProperty("status").GetString()).ToList());
    }

    [Fact]
    public async Task Mismatch_at_the_door_opens_a_dispute_and_freezes_the_order()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var d = await DealAsync(c, admin, Watches);
        await MoveAsync(c, d.Page, d.StoreOrderId, "accepted");
        await MoveAsync(c, d.Page, d.StoreOrderId, "outForDelivery");
        var r = await c.SendAsync(Api.Post($"/api/v1/orders/{d.OrderId}/delivery-answer", new { answer = "mismatch" }, d.Customer));
        Assert.True(r.IsSuccessStatusCode);
        var view = await GetAsync(c, $"/api/v1/store/orders/{d.StoreOrderId}", d.Customer);
        Assert.Equal("mismatch", view.GetProperty("dealStatus").GetString());
        Assert.False(view.GetProperty("canAnswer").GetBoolean());
        var frozen = await c.SendAsync(Req("POST", $"/api/v1/pages/me/store-orders/{d.StoreOrderId}/status", d.Page, new { status = "delivered" }));
        Assert.Equal(HttpStatusCode.Conflict, frozen.StatusCode);
    }

    [Fact]
    public async Task Page_rejecting_reopens_the_request_and_customer_cancel_refunds_the_fee()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var d = await DealAsync(c, admin, Clothes);
        await MoveAsync(c, d.Page, d.StoreOrderId, "rejected");
        var req = await GetAsync(c, $"/api/v1/requests/{d.Request}", d.Customer);
        Assert.Contains(req.GetProperty("status").GetString(), new[] { "open", "locked" });

        // Customer cancel before the page accepts: the selection fee (or free credit) goes back to the page.
        var page2 = await Api.VerifiedPageAsync(c, admin, Clothes);
        var customer2 = await Api.CustomerAsync(c);
        var request2 = await Api.RequestAsync(c, customer2, Clothes, size: "M");
        var offer2 = (await (await Api.OfferAsync(c, page2, request2)).ReadAsync()).GetProperty("id").GetString();
        var before = await GetAsync(c, "/api/v1/pages/me/wallet", page2);
        var sel2 = await (await c.SendAsync(Api.Post($"/api/v1/requests/{request2}/select", new { offerId = offer2 }, customer2))).ReadAsync();
        var d2 = new { StoreOrderId = sel2.GetProperty("storeOrderId").GetString(), Customer = customer2, Request = request2 };
        var cancel = await c.SendAsync(Api.Post($"/api/v1/store/orders/{d2.StoreOrderId}/cancel", null, d2.Customer));
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Equal("cancelled", (await cancel.ReadAsync()).GetProperty("status").GetString());
        var after = await GetAsync(c, "/api/v1/pages/me/wallet", page2);
        Assert.Equal(before.GetProperty("balance").GetInt64(), after.GetProperty("balance").GetInt64());
        Assert.Equal(before.GetProperty("freeUsed").GetInt32(), after.GetProperty("freeUsed").GetInt32());
        // Once accepted, the customer can no longer cancel from the app.
        var d3 = await DealAsync(c, admin, Clothes);
        await MoveAsync(c, d3.Page, d3.StoreOrderId, "accepted");
        Assert.Equal(HttpStatusCode.Conflict, (await c.SendAsync(Api.Post($"/api/v1/store/orders/{d3.StoreOrderId}/cancel", null, d3.Customer))).StatusCode);
        var req2 = await GetAsync(c, $"/api/v1/requests/{d2.Request}", d2.Customer);
        Assert.Contains(req2.GetProperty("status").GetString(), new[] { "open", "locked" });
    }

    // ---------- couriers ----------

    [Fact]
    public async Task Couriers_are_private_to_the_page_and_validated()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var d = await DealAsync(c, admin, Watches);
        var other = await Api.VerifiedPageAsync(c, admin, Watches);
        var mine = await CourierAsync(c, d.Page, "حسن", "+9647801234567");
        var theirs = await CourierAsync(c, other, "زيد", "07709998877");

        var list = await GetAsync(c, "/api/v1/pages/me/couriers", d.Page);
        Assert.Single(list.EnumerateArray());
        Assert.Equal("07801234567", list[0].GetProperty("phone").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await c.SendAsync(Req("POST", "/api/v1/pages/me/couriers", d.Page, new { name = "x", phone = "123" }))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(Req("PUT", $"/api/v1/pages/me/couriers/{theirs}", d.Page, new { name = "x", phone = "07701234567" }))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(Req("DELETE", $"/api/v1/pages/me/couriers/{theirs}", d.Page))).StatusCode);

        await MoveAsync(c, d.Page, d.StoreOrderId, "accepted");
        var foreign = await c.SendAsync(Req("POST", $"/api/v1/pages/me/store-orders/{d.StoreOrderId}/status", d.Page, new { status = "outForDelivery", courierId = theirs }));
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        var edit = await c.SendAsync(Req("PUT", $"/api/v1/pages/me/couriers/{mine}", d.Page, new { name = "حسن", phone = "07801234567", active = false }));
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var paused = await c.SendAsync(Req("POST", $"/api/v1/pages/me/store-orders/{d.StoreOrderId}/status", d.Page, new { status = "outForDelivery", courierId = mine }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, paused.StatusCode);
        (await c.SendAsync(Req("PUT", $"/api/v1/pages/me/couriers/{mine}", d.Page, new { name = "حسن", phone = "07801234567", active = true }))).EnsureSuccessStatusCode();
        var sent = await MoveAsync(c, d.Page, d.StoreOrderId, "outForDelivery", mine);
        Assert.Equal("حسن", sent.GetProperty("courier").GetProperty("name").GetString());

        // Deleting keeps the snapshot on the order.
        Assert.True((await c.SendAsync(Req("DELETE", $"/api/v1/pages/me/couriers/{mine}", d.Page))).IsSuccessStatusCode);
        Assert.Empty((await GetAsync(c, "/api/v1/pages/me/couriers", d.Page)).EnumerateArray());
        var view = await GetAsync(c, $"/api/v1/store/orders/{d.StoreOrderId}", d.Customer);
        Assert.Equal("حسن", view.GetProperty("courier").GetProperty("name").GetString());
    }

    // ---------- anti-leak ----------

    [Theory]
    [InlineData("كلمني على 07701234567")]
    [InlineData("تابعني @shop_iq")]
    [InlineData("اطلب من https://example.com")]
    [InlineData("رقمي ٠٧٧٠١٢٣٤٥٦٧")]
    public async Task Offer_notes_with_contact_details_are_refused_in_arabic(string note)
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var page = await Api.VerifiedPageAsync(c, admin, Clothes);
        var customer = await Api.CustomerAsync(c);
        var req = await Api.RequestAsync(c, customer, Clothes, size: "M");
        var r = await c.SendAsync(Api.Post($"/api/v1/requests/{req}/offers",
            new { price = 40000, deliveryHours = 24, condition = "new", photoKey = "offers/x.webp", note }, page));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        var pd = await r.ReadAsync();
        Assert.Equal("offer.contact", pd.GetProperty("code").GetString());
        Assert.Equal(ContactFilter.ArabicMessage, pd.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Order_notes_pins_and_reasons_are_checked_for_contact_details()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var page = await Api.VerifiedPageAsync(c, admin, Watches);
        var customer = await Api.CustomerAsync(c);
        var req = await Api.RequestAsync(c, customer, Watches);
        var offer = (await (await Api.OfferAsync(c, page, req)).ReadAsync()).GetProperty("id").GetString();

        var leak = await c.SendAsync(Api.Post($"/api/v1/requests/{req}/select", new { offerId = offer, locationNote = "دك علي 07701234567" }, customer));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, leak.StatusCode);
        var pd = await leak.ReadAsync();
        Assert.Equal("order.contact", pd.GetProperty("code").GetString());
        Assert.Equal(ContactFilter.ArabicMessage, pd.GetProperty("title").GetString());
        var half = await c.SendAsync(Api.Post($"/api/v1/requests/{req}/select", new { offerId = offer, latitude = 33.3 }, customer));
        Assert.Equal(HttpStatusCode.BadRequest, half.StatusCode);
        var far = await c.SendAsync(Api.Post($"/api/v1/requests/{req}/select", new { offerId = offer, latitude = 133.3, longitude = 44.4 }, customer));
        Assert.Equal(HttpStatusCode.BadRequest, far.StatusCode);

        var checkout = await c.SendAsync(Api.Post("/api/v1/store/checkout",
            new { address = "بغداد", note = "واتساب wa.me/9647701234567", paymentMethod = "cash" }, customer));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, checkout.StatusCode);
        Assert.Equal("order.contact", (await checkout.ReadAsync()).GetProperty("code").GetString());

        var ok = await c.SendAsync(Api.Post($"/api/v1/requests/{req}/select", new { offerId = offer }, customer));
        var id = (await ok.ReadAsync()).GetProperty("storeOrderId").GetString();
        var reason = await c.SendAsync(Req("POST", $"/api/v1/pages/me/store-orders/{id}/status", page, new { status = "rejected", reason = "اتصل على @me" }));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, reason.StatusCode);
    }

    // ---------- phone visibility ----------

    [Fact]
    public async Task Customer_phone_reaches_only_the_owning_page_in_order_details()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var page = await Api.VerifiedPageAsync(c, admin, Clothes);
        var rival = await Api.VerifiedPageAsync(c, admin, Clothes);
        var (customer, phone) = await NamedCustomerAsync(c);
        var digits = phone[^8..]; // matches local and +964 forms
        var req = await Api.RequestAsync(c, customer, Clothes, size: "M");

        // Before selection: open requests, inbox, feed and the offer itself carry no phone.
        var seen = new List<string>
        {
            await RawAsync(c, "GET", "/api/v1/pages/me/inbox", page),
            await RawAsync(c, "GET", "/api/v1/pages/me/feed", page),
            await RawAsync(c, "POST", $"/api/v1/requests/{req}/offers", page, new { price = 40000, deliveryHours = 24, condition = "new", photoKey = "offers/x.webp" }),
            await RawAsync(c, "POST", $"/api/v1/requests/{req}/offers", rival, new { price = 45000, deliveryHours = 24, condition = "new", photoKey = "offers/x.webp" }),
            await RawAsync(c, "GET", "/api/v1/pages/me/offers", page),
        };
        var offerId = (await GetAsync(c, "/api/v1/pages/me/offers", page))[0].GetProperty("id").GetString();

        var selRaw = await RawAsync(c, "POST", $"/api/v1/requests/{req}/select", customer, new { offerId });
        var sel = JsonDocument.Parse(selRaw).RootElement;
        var id = sel.GetProperty("storeOrderId").GetString()!;

        // After selection: every list, profile and board the pages can read still has no customer phone.
        seen.Add(await RawAsync(c, "GET", "/api/v1/pages/me/offers", page));
        seen.Add(await RawAsync(c, "GET", "/api/v1/pages/me/orders", page));
        seen.Add(await RawAsync(c, "GET", "/api/v1/pages/me/offers?state=chosen", page));
        seen.Add(await RawAsync(c, "GET", "/api/v1/pages/me/store-orders", page));
        seen.Add(await RawAsync(c, "GET", "/api/v1/pages/me/store-orders?status=active", page));
        seen.Add(await RawAsync(c, "GET", "/api/v1/pages/me", page));
        seen.Add(await RawAsync(c, "GET", "/api/v1/pages/me/inbox", page));
        seen.Add(await RawAsync(c, "GET", "/api/v1/pages/me/feed", page));
        seen.Add(await RawAsync(c, "GET", "/api/v1/pages/me/offers", rival));
        seen.Add(await RawAsync(c, "GET", "/api/v1/pages/me/store-orders", rival));
        seen.Add(await RawAsync(c, "GET", "/api/v1/notifications", page));
        seen.Add(await RawAsync(c, "GET", $"/api/v1/requests/{req}/offers", customer));
        foreach (var body in seen) Assert.DoesNotContain(digits, body);
        var listRow = (await GetAsync(c, "/api/v1/pages/me/offers?state=chosen", page))[0];
        Assert.Equal(JsonValueKind.Null, listRow.GetProperty("customerPhone").ValueKind);
        Assert.Equal(id, listRow.GetProperty("storeOrderId").GetString());

        // The owning page's order details: present.
        var detail = await GetAsync(c, $"/api/v1/pages/me/store-orders/{id}", page);
        Assert.Contains(digits, detail.GetProperty("customerPhone").GetString());
        var moved = await c.SendAsync(Req("POST", $"/api/v1/pages/me/store-orders/{id}/status", page, new { status = "accepted" }));
        Assert.Contains(digits, (await moved.ReadAsync()).GetProperty("customerPhone").GetString());

        // Another page: not found, and cannot move it.
        Assert.Equal(HttpStatusCode.NotFound, (await c.SendAsync(Req("GET", $"/api/v1/pages/me/store-orders/{id}", rival))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await c.SendAsync(Req("POST", $"/api/v1/pages/me/store-orders/{id}/status", rival, new { status = "outForDelivery" }))).StatusCode);
        // The offer-side order is the owner's only.
        Assert.NotEqual(HttpStatusCode.OK, (await c.SendAsync(Req("POST", $"/api/v1/orders/{sel.GetProperty("orderId").GetString()}/contacted", rival))).StatusCode);

        // Once finished, even the details drop it for the page app.
        await MoveAsync(c, page, id, "cancelled");
        var closed = await GetAsync(c, $"/api/v1/pages/me/store-orders/{id}", page);
        Assert.Equal(JsonValueKind.Null, closed.GetProperty("customerPhone").ValueKind);
    }
}
