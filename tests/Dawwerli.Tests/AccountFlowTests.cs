using System.Net;

namespace Dawwerli.Tests;

[Collection("api")]
public class AccountFlowTests(ApiFactory factory)
{
    [Fact]
    public async Task Account_deletion_requires_the_code_sent_to_my_phone()
    {
        var c = await factory.ClientAsync();
        var t = await Api.CustomerAsync(c);

        var wrong = await c.SendAsync(Api.Post("/api/v1/me/delete", new { code = "000000" }, t));
        Assert.True(wrong.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.TooManyRequests);

        var sent = await c.SendAsync(Api.Post("/api/v1/me/delete-code", null, t));
        sent.EnsureSuccessStatusCode();
        var code = (await sent.ReadAsync()).GetProperty("devCode").GetString();
        var ok = await c.SendAsync(Api.Post("/api/v1/me/delete", new { code }, t));
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
    }

    [Fact]
    public async Task Contact_message_is_stored_and_visible_to_admin()
    {
        var c = await factory.ClientAsync();
        var t = await Api.CustomerAsync(c);
        var res = await c.SendAsync(Api.Post("/api/v1/contact", new { topic = "اقتراح", text = "ضيفوا فئة الكتب رجاءً" }, t));
        res.EnsureSuccessStatusCode();
        var admin = await Api.AdminAsync(c, factory);
        var m = new HttpRequestMessage(HttpMethod.Get, "/api/admin/v1/contact");
        m.Headers.Authorization = new("Bearer", admin);
        var list = await (await c.SendAsync(m)).ReadAsync();
        Assert.Contains(list.EnumerateArray(), x => x.GetProperty("text").GetString() == "ضيفوا فئة الكتب رجاءً");
    }
}
