namespace Dawwerli.Infrastructure.Persistence.LiveSeed;

/// <summary>
/// "Seed" configuration section. <see cref="Live"/> turns on the one-time guarded seed at startup
/// (env <c>Seed__Live=true</c>); the counts are also the defaults of the <c>seed-live</c> command.
/// Defaults are sized for a small server.
/// </summary>
public sealed class LiveSeedOptions
{
    public const string Section = "Seed";

    /// <summary>Wipe non-admin data and seed once at startup, guarded by the marker row. Off by default.</summary>
    public bool Live { get; set; }
    public int Pages { get; set; } = 500;
    /// <summary>How many of the pages (the first ones) get products.</summary>
    public int ProductPages { get; set; } = 100;
    public int MinProductsPerPage { get; set; } = 10;
    public int MaxProductsPerPage { get; set; } = 20;
    public int Customers { get; set; } = 50;
    /// <summary>Exact number of offers, spread over open, selected, delivered/rated and expired requests.</summary>
    public int Offers { get; set; } = 50;
    public int StoreOrders { get; set; } = 90;
    public int Banners { get; set; } = 5;
    public int FeaturedAds { get; set; } = 4;
    public int FeaturedPages { get; set; } = 8;
    /// <summary>Spread of created-at dates, in days back from now.</summary>
    public int HistoryDays { get; set; } = 180;
    /// <summary>Random seed: the same seed gives the same names, prices and spread.</summary>
    public int RandomSeed { get; set; } = 42;
    /// <summary>
    /// Optional shared password for web seller-portal logins of the product pages (email «handle»@sellers.dawwerli.test).
    /// Left empty, no seller accounts are created, so no known credential ships with the data.
    /// </summary>
    public string? SellerPassword { get; set; }

    public LiveSeedOptions Copy() => (LiveSeedOptions)MemberwiseClone();

    public string? Validate()
    {
        if (Pages < 1 || Customers < 1) return "pages and customers must be at least 1";
        if (ProductPages < 0 || ProductPages > Pages) return "product pages must be between 0 and pages";
        if (MinProductsPerPage < 0 || MaxProductsPerPage < MinProductsPerPage) return "products per page range is invalid";
        if (Offers < 0 || StoreOrders < 0 || Banners < 0 || FeaturedAds < 0 || FeaturedPages < 0) return "counts cannot be negative";
        if (HistoryDays < 14) return "history days must be at least 14";
        if (Pages > 20_000 || Customers > 20_000 || Offers > 20_000 || StoreOrders > 20_000) return "counts above 20000 are not supported";
        return null;
    }
}
