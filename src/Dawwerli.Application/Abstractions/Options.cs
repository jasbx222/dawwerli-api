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
}
