using System.Net;

namespace Dawwerli.Tests;

[Collection("api")]
public class PasswordLoginTests(ApiFactory factory)
{
    private static async Task<string> SignUpAsync(HttpClient c, string phone, string role, string password)
    {
        var token = await Api.LoginAsync(c, role, phone);
        var set = await c.SendAsync(Api.Post("/api/v1/auth/password", new { password }, token));
        Assert.Equal(HttpStatusCode.NoContent, set.StatusCode);
        return token;
    }

    private static Task<HttpResponseMessage> LoginAsync(HttpClient c, string phone, string password, string role = "customer") =>
        c.SendAsync(Api.Post("/api/v1/auth/login", new { phone, password, role, deviceId = "t" }, null));

    [Fact]
    public async Task Customer_signs_up_sets_password_then_logs_in_with_phone_and_password()
    {
        var c = await factory.ClientAsync();
        var phone = Api.NewPhone();
        await SignUpAsync(c, phone, "customer", "secret-pass-1");
        var res = await LoginAsync(c, phone, "secret-pass-1");
        res.EnsureSuccessStatusCode();
        var body = await res.ReadAsync();
        Assert.False(string.IsNullOrEmpty(body.GetProperty("accessToken").GetString()));
        Assert.True(body.GetProperty("hasPassword").GetBoolean());
        Assert.Equal("customer", body.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Seller_without_page_logs_in_and_is_told_to_create_page()
    {
        var c = await factory.ClientAsync();
        var phone = Api.NewPhone();
        await SignUpAsync(c, phone, "page", "seller-pass-1");
        var body = await (await LoginAsync(c, phone, "seller-pass-1", "page")).ReadAsync();
        Assert.Equal("page", body.GetProperty("role").GetString());
        Assert.True(body.GetProperty("needsProfile").GetBoolean());
    }

    [Fact]
    public async Task Wrong_password_and_unknown_phone_get_the_same_generic_error()
    {
        var c = await factory.ClientAsync();
        var phone = Api.NewPhone();
        await SignUpAsync(c, phone, "customer", "right-pass-1");
        var wrong = await LoginAsync(c, phone, "wrong-pass-1");
        var unknown = await LoginAsync(c, Api.NewPhone(), "whatever-1");
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal((await wrong.ReadAsync()).GetProperty("code").GetString(), (await unknown.ReadAsync()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Too_many_wrong_passwords_lock_the_account_even_for_the_right_one()
    {
        var c = await factory.ClientAsync();
        var phone = Api.NewPhone();
        await SignUpAsync(c, phone, "customer", "right-pass-1");
        for (var i = 0; i < 5; i++) await LoginAsync(c, phone, "wrong-pass-1");
        var res = await LoginAsync(c, phone, "right-pass-1");
        Assert.Equal(HttpStatusCode.TooManyRequests, res.StatusCode);
    }

    [Fact]
    public async Task Short_password_is_rejected()
    {
        var c = await factory.ClientAsync();
        var token = await Api.LoginAsync(c, "customer");
        var res = await c.SendAsync(Api.Post("/api/v1/auth/password", new { password = "123" }, token));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Forgot_password_resets_with_otp_and_old_password_stops_working()
    {
        var c = await factory.ClientAsync();
        var phone = Api.NewPhone();
        await SignUpAsync(c, phone, "customer", "old-pass-111");
        var otp = await (await c.SendAsync(Api.Post("/api/v1/auth/otp/request", new { phone }, null))).ReadAsync();
        var bad = await c.SendAsync(Api.Post("/api/v1/auth/password/reset", new { phone, code = "999999x", password = "new-pass-111", role = "customer" }, null));
        Assert.False(bad.IsSuccessStatusCode);
        var reset = await c.SendAsync(Api.Post("/api/v1/auth/password/reset",
            new { phone, code = otp.GetProperty("devCode").GetString(), password = "new-pass-111", role = "customer", deviceId = "t" }, null));
        reset.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(c, phone, "old-pass-111")).StatusCode);
        (await LoginAsync(c, phone, "new-pass-111")).EnsureSuccessStatusCode();
    }
}
