using System.Net;
using System.Reflection;
using System.Net.Http.Json;
using Dawwerli.Api.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace Dawwerli.Tests;

/// <summary>Every admin route must reject anonymous callers and customer/page tokens.</summary>
[Collection("api")]
public class AdminAuthorizationTests(ApiFactory factory)
{
    public static IEnumerable<object[]> AdminRoutes()
    {
        var prefix = typeof(AdminController).GetCustomAttribute<RouteAttribute>()!.Template;
        foreach (var m in typeof(AdminController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        foreach (var a in m.GetCustomAttributes<HttpMethodAttribute>())
        {
            var path = "/" + prefix + "/" + (a.Template ?? "");
            path = System.Text.RegularExpressions.Regex.Replace(path, @"\{[^}]*:guid\}", Guid.NewGuid().ToString());
            path = System.Text.RegularExpressions.Regex.Replace(path, @"\{[^}]*:int\}", "1");
            foreach (var verb in a.HttpMethods) yield return [verb, path];
        }
    }

    private static HttpRequestMessage Req(string verb, string path, string? token)
    {
        var m = new HttpRequestMessage(new HttpMethod(verb), path);
        if (verb != "GET" && verb != "DELETE") m.Content = JsonContent.Create(new { });
        if (token is not null) m.Headers.Authorization = new("Bearer", token);
        return m;
    }

    [Theory, MemberData(nameof(AdminRoutes))]
    public async Task Admin_route_rejects_anonymous_customer_and_page(string verb, string path)
    {
        var c = await factory.ClientAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Req(verb, path, null))).StatusCode);
        var customer = await Api.CustomerAsync(c);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(Req(verb, path, customer))).StatusCode);
        var page = await Api.LoginAsync(c, "page");
        Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(Req(verb, path, page))).StatusCode);
    }

    [Fact]
    public async Task Admin_login_rejects_wrong_password_and_admin_token_reaches_admin_routes()
    {
        var c = await factory.ClientAsync();
        var bad = await c.PostAsJsonAsync("/api/v1/auth/admin/login", new { email = ApiFactory.AdminEmail, password = "wrong-password" });
        Assert.False(bad.IsSuccessStatusCode);
        var admin = await Api.AdminAsync(c, factory);
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Req("GET", "/api/admin/v1/users", admin))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await c.SendAsync(Req("GET", "/api/admin/v1/kpis", admin))).StatusCode);
    }

    [Fact]
    public async Task Tampered_admin_token_is_rejected()
    {
        var c = await factory.ClientAsync();
        var admin = await Api.AdminAsync(c, factory);
        var forged = admin[..^4] + (admin[^4..] == "AAAA" ? "BBBB" : "AAAA");
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(Req("GET", "/api/admin/v1/kpis", forged))).StatusCode);
    }

    [Fact]
    public async Task Only_admin_can_upload_category_images()
    {
        var c = await factory.ClientAsync();
        var customer = await Api.CustomerAsync(c);
        var form = new MultipartFormDataContent { { new ByteArrayContent([1, 2, 3]), "file", "a.png" }, { new StringContent("categories"), "kind" } };
        var m = new HttpRequestMessage(HttpMethod.Post, "/api/v1/media") { Content = form };
        m.Headers.Authorization = new("Bearer", customer);
        Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(m)).StatusCode);
    }
}
