using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Dawwerli.Tests;

[Collection("api")]
public class AdminRolesAndTotpTests(ApiFactory factory)
{
    private static HttpRequestMessage Req(string verb, string path, string token, object? body = null)
    {
        var m = new HttpRequestMessage(new HttpMethod(verb), path);
        if (verb is "POST" or "PUT") m.Content = JsonContent.Create(body ?? new { });
        m.Headers.Authorization = new("Bearer", token);
        return m;
    }

    private async Task<string> NewAdminAsync(HttpClient c, string role)
    {
        var super = await Api.AdminAsync(c, factory);
        var email = $"{role.ToLowerInvariant()}-{Guid.NewGuid():N}@test.local";
        var password = Guid.NewGuid().ToString("N");
        var res = await c.SendAsync(Req("POST", "/api/admin/v1/admins", super, new { email, name = role, password, adminRole = role }));
        res.EnsureSuccessStatusCode();
        return await Api.AdminLoginAsync(c, email, password);
    }

    [Fact]
    public async Task Password_only_login_returns_tokens_when_totp_is_off()
    {
        var c = await factory.ClientAsync();
        var res = await (await c.PostAsJsonAsync("/api/v1/auth/admin/login", new { email = ApiFactory.AdminEmail, password = factory.AdminPassword })).ReadAsync();
        Assert.Equal("ok", res.GetProperty("status").GetString());
        Assert.False(string.IsNullOrEmpty(res.GetProperty("auth").GetProperty("accessToken").GetString()));
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/v1/auth/admin/login", new { email = ApiFactory.AdminEmail, password = "wrong" })).StatusCode);
    }

    public static IEnumerable<object[]> Matrix() => new[]
    {
        // route, verb, moderator allowed, finance allowed
        new object[] { "GET", "/api/admin/v1/topups", false, true },
        new object[] { "PUT", "/api/admin/v1/settings/pricing", false, true },
        new object[] { "POST", $"/api/admin/v1/pages/{Guid.NewGuid()}/wallet/adjust", false, true },
        new object[] { "GET", "/api/admin/v1/disputes", true, false },
        new object[] { "GET", "/api/admin/v1/reports", true, false },
        new object[] { "GET", "/api/admin/v1/users", true, false },
        new object[] { "GET", "/api/admin/v1/ads", true, false },
        new object[] { "POST", $"/api/admin/v1/pages/{Guid.NewGuid()}/verify", true, false },
        new object[] { "POST", $"/api/admin/v1/pages/{Guid.NewGuid()}/unsuspend", true, false },
        new object[] { "GET", "/api/admin/v1/keywords", true, false },
        new object[] { "PUT", "/api/admin/v1/settings", false, false },
        new object[] { "GET", "/api/admin/v1/categories", false, false },
        new object[] { "GET", "/api/admin/v1/audit", false, false },
        new object[] { "GET", "/api/admin/v1/admins", false, false },
        new object[] { "POST", "/api/admin/v1/admins", false, false },
        new object[] { "GET", "/api/admin/v1/kpis", true, true },
        new object[] { "GET", "/api/admin/v1/settings", true, true },
    };

    private string? _mod, _fin;

    [Fact]
    public async Task Sub_roles_are_enforced_per_endpoint()
    {
        var c = await factory.ClientAsync();
        _mod ??= await NewAdminAsync(c, "Moderator");
        _fin ??= await NewAdminAsync(c, "Finance");
        foreach (var row in Matrix())
        {
            var (verb, path, mod, fin) = ((string)row[0], (string)row[1], (bool)row[2], (bool)row[3]);
            var m = (await c.SendAsync(Req(verb, path, _mod))).StatusCode;
            var f = (await c.SendAsync(Req(verb, path, _fin))).StatusCode;
            Assert.True(mod ? m != HttpStatusCode.Forbidden : m == HttpStatusCode.Forbidden, $"moderator {verb} {path} -> {m}");
            Assert.True(fin ? f != HttpStatusCode.Forbidden : f == HttpStatusCode.Forbidden, $"finance {verb} {path} -> {f}");
        }
        var me = await (await c.SendAsync(Req("GET", "/api/admin/v1/me", _fin))).ReadAsync();
        Assert.Equal("Finance", me.GetProperty("adminRole").GetString());
    }

    [Fact]
    public async Task Super_admin_cannot_change_own_role_and_audit_is_paged()
    {
        var c = await factory.ClientAsync();
        var super = await Api.AdminAsync(c, factory);
        var admins = await (await c.SendAsync(Req("GET", "/api/admin/v1/admins", super))).ReadAsync();
        var self = admins.EnumerateArray().First(a => a.GetProperty("email").GetString() == ApiFactory.AdminEmail).GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.Conflict, (await c.SendAsync(Req("PUT", $"/api/admin/v1/admins/{self}/role", super, new { adminRole = "Finance" }))).StatusCode);

        await NewAdminAsync(c, "Finance");
        var page = await (await c.SendAsync(Req("GET", "/api/admin/v1/audit?page=1&pageSize=1", super))).ReadAsync();
        Assert.Equal(1, page.GetProperty("items").GetArrayLength());
        Assert.True(page.GetProperty("total").GetInt32() >= 1);
        Assert.Equal(1, page.GetProperty("pageSize").GetInt32());
        var huge = await (await c.SendAsync(Req("GET", "/api/admin/v1/audit?pageSize=100000", super))).ReadAsync();
        Assert.True(huge.GetProperty("pageSize").GetInt32() <= 200);
    }

    [Fact]
    public async Task Unsuspend_restores_a_suspended_page()
    {
        var c = await factory.ClientAsync();
        var super = await Api.AdminAsync(c, factory);
        await Api.VerifiedPageAsync(c, super, 1);
        var pages = await (await c.SendAsync(Req("GET", "/api/admin/v1/pages?status=verified", super))).ReadAsync();
        var id = pages.EnumerateArray().First().GetProperty("id").GetString();
        Assert.Equal(HttpStatusCode.Conflict, (await c.SendAsync(Req("POST", $"/api/admin/v1/pages/{id}/unsuspend", super, new { reason = "x" }))).StatusCode);
        (await c.SendAsync(Req("POST", $"/api/admin/v1/pages/{id}/suspend", super, new { days = 3, reason = "test" }))).EnsureSuccessStatusCode();
        var suspended = await (await c.SendAsync(Req("GET", "/api/admin/v1/pages?status=suspended", super))).ReadAsync();
        Assert.Contains(suspended.EnumerateArray(), p => p.GetProperty("id").GetString() == id && p.GetProperty("suspendedUntil").ValueKind == JsonValueKind.String);
        Assert.Equal(HttpStatusCode.NoContent, (await c.SendAsync(Req("POST", $"/api/admin/v1/pages/{id}/unsuspend", super, new { reason = "appeal accepted" }))).StatusCode);
        var verified = await (await c.SendAsync(Req("GET", "/api/admin/v1/pages?status=verified", super))).ReadAsync();
        Assert.Contains(verified.EnumerateArray(), p => p.GetProperty("id").GetString() == id);
    }
}

public class AdminTotpOnTests(TotpApiFactory factory) : IClassFixture<TotpApiFactory>
{
    private static HttpRequestMessage Req(string verb, string path, string token, object? body = null)
    {
        var m = new HttpRequestMessage(new HttpMethod(verb), path);
        if (verb is "POST" or "PUT") m.Content = JsonContent.Create(body ?? new { });
        m.Headers.Authorization = new("Bearer", token);
        return m;
    }

    [Fact]
    public async Task Totp_enrollment_wrong_code_replay_and_code_required()
    {
        var c = await factory.ClientAsync();
        var super = await Api.AdminAsync(c, factory);
        var email = $"totp-{Guid.NewGuid():N}@test.local";
        var password = Guid.NewGuid().ToString("N");
        (await c.SendAsync(Req("POST", "/api/admin/v1/admins", super, new { email, name = "t", password, adminRole = "Moderator" }))).EnsureSuccessStatusCode();

        var enroll = await (await c.PostAsJsonAsync("/api/v1/auth/admin/login", new { email, password })).ReadAsync();
        Assert.Equal("enroll", enroll.GetProperty("status").GetString());
        Assert.False(enroll.TryGetProperty("auth", out var a) && a.ValueKind != JsonValueKind.Null);
        var secret = enroll.GetProperty("secret").GetString()!;
        Assert.StartsWith("otpauth://totp/", enroll.GetProperty("otpAuthUri").GetString());

        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/v1/auth/admin/login", new { email, password, code = "000000" == Api.TotpCode(secret) ? "111111" : "000000" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/v1/auth/admin/login", new { email, password = "wrong", code = Api.TotpCode(secret) })).StatusCode);

        var code = Api.TotpCode(secret);
        var ok = await c.PostAsJsonAsync("/api/v1/auth/admin/login", new { email, password, code });
        ok.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.PostAsJsonAsync("/api/v1/auth/admin/login", new { email, password, code })).StatusCode);

        var again = await (await c.PostAsJsonAsync("/api/v1/auth/admin/login", new { email, password })).ReadAsync();
        Assert.Equal("code_required", again.GetProperty("status").GetString());
        Assert.False(again.TryGetProperty("secret", out var sct) && sct.ValueKind != JsonValueKind.Null);
    }

}
