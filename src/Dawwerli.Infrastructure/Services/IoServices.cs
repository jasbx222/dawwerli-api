using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Dawwerli.Application.Abstractions;
using Dawwerli.Domain.Entities;
using Dawwerli.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dawwerli.Infrastructure.Services;

/// <summary>Local-disk object storage with HMAC-signed, expiring URLs. Swap for S3/MinIO behind <see cref="IStorage"/>.</summary>
public sealed class LocalStorage(IOptions<StorageOptions> options) : IStorage
{
    private readonly StorageOptions _o = options.Value;

    public async Task<string> SaveAsync(Stream content, string extension, string folder, CancellationToken ct = default)
    {
        var key = $"{folder}/{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var f = File.Create(path);
        await content.CopyToAsync(f, ct);
        return key;
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var path = PathFor(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public string? Url(string? key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (key.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return key;
        var exp = DateTimeOffset.UtcNow.AddMinutes(_o.UrlTtlMinutes).ToUnixTimeSeconds();
        return $"{_o.PublicBaseUrl.TrimEnd('/')}/media/{key}?exp={exp}&sig={Sign(key, exp)}";
    }

    public string Sign(string key, long exp) =>
        Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(_o.SigningKey), Encoding.UTF8.GetBytes($"{key}|{exp}")))[..32];

    public bool Validate(string key, long exp, string sig) =>
        exp >= DateTimeOffset.UtcNow.ToUnixTimeSeconds() &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Sign(key, exp)), Encoding.UTF8.GetBytes(sig));

    public string PathFor(string key)
    {
        var root = Path.GetFullPath(_o.RootPath);
        var full = Path.GetFullPath(Path.Combine(root, key));
        if (!full.StartsWith(root, StringComparison.Ordinal)) throw new UnauthorizedAccessException("Invalid key");
        return full;
    }
}

/// <summary>Development sender: logs only a masked phone (never the code).</summary>
public sealed class LogSmsSender(ILogger<LogSmsSender> log) : ISmsSender
{
    public Task<bool> SendOtpAsync(string phone, string code, OtpChannel channel, CancellationToken ct = default)
    {
        log.LogInformation("OTP via {Channel} to ***{Tail}", channel, phone[^Math.Min(3, phone.Length)..]);
        return Task.FromResult(true);
    }
}

/// <summary>Generic HTTP SMS/WhatsApp gateway (configure Sms:Url/ApiKey/SenderId/Template).</summary>
public sealed class HttpSmsSender(HttpClient http, IOptions<SmsOptions> options, ILogger<HttpSmsSender> log) : ISmsSender
{
    public async Task<bool> SendOtpAsync(string phone, string code, OtpChannel channel, CancellationToken ct = default)
    {
        var o = options.Value;
        using var req = new HttpRequestMessage(HttpMethod.Post, o.Url)
        {
            Content = JsonContent.Create(new { to = phone, from = o.SenderId, channel = channel.ToString().ToLowerInvariant(), text = string.Format(o.Template, code) }),
        };
        req.Headers.Authorization = new("Bearer", o.ApiKey);
        try
        {
            var res = await http.SendAsync(req, ct);
            return res.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            log.LogWarning(ex, "SMS gateway failed");
            return false;
        }
    }
}

/// <summary>Placeholder until FCM credentials exist: logs the push. In-app notifications are still stored.</summary>
public sealed class LogPushSender(ILogger<LogPushSender> log) : IPushSender
{
    public Task SendAsync(IReadOnlyCollection<string> tokens, string title, string body, string route, CancellationToken ct = default)
    {
        if (tokens.Count > 0) log.LogInformation("Push to {Count} devices: {Route}", tokens.Count, route);
        return Task.CompletedTask;
    }
}

public sealed class Notifier(AppDbContext db, IPushSender push) : INotifier
{
    public async Task NotifyAsync(Guid userId, string title, string body, string route, CancellationToken ct = default)
    {
        db.Notifications.Add(new Notification { UserId = userId, Title = title, Body = body, Route = route });
        await db.SaveChangesAsync(ct);
        var tokens = await db.Devices.Where(d => d.UserId == userId).Select(d => d.PushToken).ToListAsync(ct);
        await push.SendAsync(tokens, title, body, route, ct);
    }
}
