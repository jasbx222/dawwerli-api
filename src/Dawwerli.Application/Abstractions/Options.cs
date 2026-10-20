namespace Dawwerli.Application.Abstractions;

public sealed class RegionOptions
{
    public const string Section = "Region";
    public string TimeZone { get; set; } = "";
    public string CountryCode { get; set; } = "";
    public string WhatsAppBaseUrl { get; set; } = "";
}

public sealed class OtpOptions
{
    public const string Section = "Otp";
    /// <summary>Development only: echo the code in the API response so the app can be tested without an SMS gateway.</summary>
    public bool ExposeCodeInResponse { get; set; }
    /// <summary>Test deployments only: when set, every OTP equals this code and no SMS is sent.</summary>
    public string? FixedCode { get; set; }
}

/// <summary>Phone + password sign-in for customers and page owners.</summary>
public sealed class UserAuthOptions
{
    public const string Section = "UserAuth";
    public int MinPasswordLength { get; set; } = 8;
    public int MaxPasswordLength { get; set; } = 128;
    /// <summary>Wrong passwords in a row before the account is temporarily locked.</summary>
    public int MaxFailedLogins { get; set; } = 5;
    public int LockMinutes { get; set; } = 15;
}
