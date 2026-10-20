namespace Dawwerli.Infrastructure.Services;

public sealed class JwtOptions
{
    public const string Section = "Jwt";
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public string SigningKey { get; set; } = "";
    public int AccessMinutes { get; set; }
    public int RefreshDays { get; set; }
}

public sealed class SecurityOptions
{
    public const string Section = "Security";
    public string PhoneHashKey { get; set; } = "";
    public string DataProtectionKeysPath { get; set; } = "";
}

public sealed class StorageOptions
{
    public const string Section = "Storage";
    public string RootPath { get; set; } = "";
    public string PublicBaseUrl { get; set; } = "";
    public string SigningKey { get; set; } = "";
    public int UrlTtlMinutes { get; set; }
    public long MaxUploadBytes { get; set; }
    public string[] AllowedExtensions { get; set; } = [];
}

public sealed class SmsOptions
{
    public const string Section = "Sms";
    /// <summary>"Log" (development) or "Http" (generic gateway webhook).</summary>
    public string Provider { get; set; } = "";
    public string Url { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string SenderId { get; set; } = "";
    public string Template { get; set; } = "";
}

public sealed class AdminSeedOptions
{
    public const string Section = "AdminSeed";
    public string Email { get; set; } = "";
    public string Password { get; set; } = "";
}

public sealed class JobsOptions
{
    public const string Section = "Jobs";
    public int IntervalSeconds { get; set; }
    public int IdempotencyHours { get; set; }
    public int SettingsCacheSeconds { get; set; }
}
