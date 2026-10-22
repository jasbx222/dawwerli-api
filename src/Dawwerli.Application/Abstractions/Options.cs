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

/// <summary>Store limits and paging (appsettings.Store.json, "Store").</summary>
public sealed class StoreOptions
{
    public const string Section = "Store";
    public const string ProductImageFolder = "products";
    public int DefaultMaxProductsPerPage { get; set; }
    public int MaxImagesPerProduct { get; set; }
    public int NameMaxLength { get; set; }
    public int DescriptionMaxLength { get; set; }
    public long MaxPrice { get; set; }
    public int MaxStock { get; set; }
    public int MaxCartQuantity { get; set; }
    public int AddressMaxLength { get; set; }
    public int NoteMaxLength { get; set; }
    public int DefaultPageSize { get; set; }
    public int MaxPageSize { get; set; }
    public int TopProductsCount { get; set; }
    /// <summary>Home «top-rated pages» strip: how many pages, and products per page.</summary>
    public int TopRatedPages { get; set; }
    public int TopRatedProductsPerPage { get; set; }
    public int RatingCommentMaxLength { get; set; }
}

/// <summary>Web seller portal sign-in ("SellerPortal").</summary>
public sealed class SellerPortalOptions
{
    public const string Section = "SellerPortal";
    public int AccessMinutes { get; set; }
    public int MinPasswordLength { get; set; }
    public int MaxFailedLogins { get; set; }
    public int LockMinutes { get; set; }
}

/// <summary>Electronic payment gateway ("Payments"). Empty Provider = electronic payment off.</summary>
public sealed class PaymentOptions
{
    public const string Section = "Payments";
    public string Provider { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string ReturnUrl { get; set; } = "";
}
