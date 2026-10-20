using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dawwerli.Application.Abstractions;
using Dawwerli.Domain.Entities;
using Dawwerli.Domain.Rules;
using Dawwerli.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Dawwerli.Infrastructure.Services;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}

public static class AppClaims
{
    public const string PageId = "page_id";
}

public sealed class JwtTokenService(IOptions<JwtOptions> options) : ITokenService
{
    private readonly JwtOptions _o = options.Value;
    public int AccessTokenSeconds => _o.AccessMinutes * 60;
    public int RefreshTokenDays => _o.RefreshDays;

    public string CreateAccessToken(User user, Guid? pageId)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.Role, user.Role.ToString()),
        };
        if (pageId is { } p) claims.Add(new Claim(AppClaims.PageId, p.ToString()));
        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_o.SigningKey)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(_o.Issuer, _o.Audience, claims, expires: DateTime.UtcNow.AddMinutes(_o.AccessMinutes), signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public string NewOpaqueToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public string HashToken(string token) => Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(_o.SigningKey), Encoding.UTF8.GetBytes(token)));
}

/// <summary>Phone encrypted at rest (Data Protection) + HMAC hash for lookups. Input normalized to E.164 digits.</summary>
public sealed class PhoneProtector(IDataProtectionProvider dp, IOptions<SecurityOptions> security, IOptions<RegionOptions> region) : IPhoneProtector
{
    private readonly IDataProtector _p = dp.CreateProtector("phone.v1");
    private readonly byte[] _key = Encoding.UTF8.GetBytes(security.Value.PhoneHashKey);

    public string Normalize(string phone)
    {
        var digits = new string(Domain.Rules.ContactFilter.Normalize(phone).Where(char.IsDigit).ToArray());
        var cc = region.Value.CountryCode;
        if (digits.StartsWith("00")) digits = digits[2..];
        if (digits.StartsWith('0')) digits = cc + digits[1..];
        else if (!digits.StartsWith(cc)) digits = cc + digits;
        return "+" + digits;
    }

    public string Hash(string phone) => Convert.ToHexString(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes(Normalize(phone))));
    public string Encrypt(string phone) => _p.Protect(Normalize(phone));
    public string Decrypt(string cipher) => string.IsNullOrEmpty(cipher) ? "" : _p.Unprotect(cipher);
}

public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int Iterations = 210_000, SaltBytes = 16, HashBytes = 32;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashBytes);
        return $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string stored)
    {
        var parts = stored.Split('.');
        if (parts.Length != 2) return false;
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(parts[0]), Iterations, HashAlgorithmName.SHA256, HashBytes);
        return CryptographicOperations.FixedTimeEquals(hash, Convert.FromBase64String(parts[1]));
    }
}

/// <summary>Business rules from the settings row, cached briefly.</summary>
public sealed class RulesProvider(AppDbContext db, IMemoryCache cache, IOptions<JobsOptions> jobs) : IRulesProvider
{
    private const string CacheKey = "business-rules";
    public const int SettingsId = 1;

    public async Task<BusinessRules> GetAsync(CancellationToken ct = default) =>
        (await cache.GetOrCreateAsync(CacheKey, async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(jobs.Value.SettingsCacheSeconds);
            var row = await db.Settings.AsNoTracking().FirstAsync(x => x.Id == SettingsId, ct);
            return JsonSerializer.Deserialize<BusinessRules>(row.Data)!;
        }))!;

    public async Task UpdateAsync(BusinessRules rules, CancellationToken ct = default)
    {
        var row = await db.Settings.FirstAsync(x => x.Id == SettingsId, ct);
        row.Data = JsonSerializer.Serialize(rules);
        row.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        cache.Remove(CacheKey);
    }
}

/// <summary>Copy texts (Arabic) from the "Texts" configuration section.</summary>
public sealed class ConfigTextCatalog(IConfiguration config) : ITextCatalog
{
    public string Get(string key, params object[] args)
    {
        var template = config[$"Texts:{key}"] ?? key;
        return args.Length == 0 ? template : string.Format(template, args);
    }
}
