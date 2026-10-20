using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Dawwerli.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Dawwerli.Tests;

/// <summary>Boots the real API against a throw-away PostgreSQL database (DAWWERLI_TEST_DB).</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@test.local";
    public readonly string AdminPassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
    private readonly string _db = Environment.GetEnvironmentVariable("DAWWERLI_TEST_DB")
        ?? "Host=localhost;Port=5432;Database=dawwerli_test;Username=postgres;Password=postgres";

    protected override void ConfigureWebHost(IWebHostBuilder b)
    {
        string Secret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        b.UseEnvironment("Testing");
        b.UseSetting("ConnectionStrings:Default", _db);
        b.UseSetting("Jwt:SigningKey", Secret());
        b.UseSetting("Security:PhoneHashKey", Secret());
        b.UseSetting("Storage:SigningKey", Secret());
        b.UseSetting("Storage:RootPath", Path.Combine(Path.GetTempPath(), "dawwerli-test-storage"));
        b.UseSetting("Storage:PublicBaseUrl", "http://localhost");
        b.UseSetting("Otp:ExposeCodeInResponse", "true");
        b.UseSetting("AdminSeed:Email", AdminEmail);
        b.UseSetting("AdminSeed:Password", AdminPassword);
        b.UseSetting("Jobs:Enabled", "false");
        b.UseSetting("RateLimits:OtpPerMinute", "10000");
        b.UseSetting("RateLimits:GlobalPerMinute", "100000");
        b.UseSetting("RateLimits:AdminLoginPerMinute", "10000");
        b.UseSetting("Database:MigrateAndSeedOnStartup", "false");
    }

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private bool _ready;

    public async Task<HttpClient> ClientAsync()
    {
        await Gate.WaitAsync();
        try
        {
            if (!_ready)
            {
                using var scope = Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await db.Database.EnsureDeletedAsync();
                await scope.ServiceProvider.GetRequiredService<Seeder>().RunAsync();
                _ready = true;
            }
        }
        finally { Gate.Release(); }
        return CreateClient();
    }
}

public static class Api
{
    private static int _phone = 100000;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static string NewPhone() => $"0770{Interlocked.Increment(ref _phone):D7}";

    public static async Task<JsonElement> ReadAsync(this HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>(Json));

    public static HttpRequestMessage Post(string url, object? body, string? token)
    {
        var m = new HttpRequestMessage(HttpMethod.Post, url) { Content = body is null ? null : JsonContent.Create(body) };
        if (token is not null) m.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        m.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        return m;
    }

    public static async Task<string> LoginAsync(HttpClient c, string role, string? phone = null)
    {
        phone ??= NewPhone();
        var otp = await (await c.SendAsync(Post("/api/v1/auth/otp/request", new { phone }, null))).ReadAsync();
        var res = await (await c.SendAsync(Post("/api/v1/auth/otp/verify", new { phone, code = otp.GetProperty("devCode").GetString(), role, deviceId = "t" }, null))).ReadAsync();
        return res.GetProperty("accessToken").GetString()!;
    }

    public static async Task<string> AdminAsync(HttpClient c, ApiFactory f) =>
        (await (await c.SendAsync(Post("/api/v1/auth/admin/login", new { email = ApiFactory.AdminEmail, password = f.AdminPassword }, null))).ReadAsync())
        .GetProperty("accessToken").GetString()!;

    /// <summary>Registers and verifies a page; returns a token that carries the page id.</summary>
    public static async Task<string> VerifiedPageAsync(HttpClient c, string admin, int categoryId, bool earlyAccess = false)
    {
        var phone = NewPhone();
        var t = await LoginAsync(c, "page", phone);
        var page = await (await c.SendAsync(Post("/api/v1/pages/register",
            new { name = $"p{phone}", instagram = $"p{phone}", categoryIds = new[] { categoryId }, governorateIds = new[] { 1 }, acceptTerms = true }, t))).ReadAsync();
        (await c.SendAsync(Post($"/api/admin/v1/pages/{page.GetProperty("id").GetString()}/verify", null, admin))).EnsureSuccessStatusCode();
        t = await LoginAsync(c, "page", phone);
        if (earlyAccess) (await c.SendAsync(Post("/api/v1/pages/me/subscriptions/early-access", null, t))).EnsureSuccessStatusCode();
        return t;
    }

    public static async Task<string> CustomerAsync(HttpClient c)
    {
        var t = await LoginAsync(c, "customer");
        var m = new HttpRequestMessage(HttpMethod.Put, "/api/v1/me/profile") { Content = JsonContent.Create(new { name = "زبون", governorateId = 1, area = "المنصور" }) };
        m.Headers.Authorization = new AuthenticationHeaderValue("Bearer", t);
        (await c.SendAsync(m)).EnsureSuccessStatusCode();
        return t;
    }

    public static async Task<string> RequestAsync(HttpClient c, string customer, int categoryId, string? size = null)
    {
        var r = await c.SendAsync(Post("/api/v1/requests", new
        {
            mode = "photo", mediaKey = "requests/x.webp", description = "طلب تجربة", categoryId, size, condition = "new",
            maxPrice = 60000, governorateId = 1, readyToBuy = true,
        }, customer));
        r.EnsureSuccessStatusCode();
        return (await r.ReadAsync()).GetProperty("id").GetString()!;
    }

    public static Task<HttpResponseMessage> OfferAsync(HttpClient c, string page, string requestId, long price = 50000) =>
        c.SendAsync(Post($"/api/v1/requests/{requestId}/offers", new { price, deliveryHours = 24, condition = "new", photoKey = "offers/x.webp" }, page));
}
