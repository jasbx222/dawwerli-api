namespace Dawwerli.Application.Contracts;

// Seller portal auth
public sealed record SellerLoginDto(string Email, string Password);
public sealed record SellerSessionDto(string AccessToken, int ExpiresIn, Guid PageId, string PageName);
public sealed record SellerMeDto(Guid AccountId, string Email, Guid PageId, string PageName, string PageStatus, int ProductCount, int ProductLimit);

// Products
public sealed record UpsertProductDto(string Name, string? Description, long Price, string[] ImageKeys, int CategoryId, int Stock, bool Active = true);
public sealed record SellerProductDto(Guid Id, string Name, string Description, long Price, IReadOnlyList<string> ImageKeys, IReadOnlyList<string?> ImageUrls,
    int CategoryId, int Stock, bool Active, bool HiddenByAdmin, bool Available, DateTime CreatedAt, double Rating = 0, int RatingCount = 0);
public sealed record ProductCardDto(Guid Id, string Name, long Price, string? ImageUrl, bool Available,
    string Description = "", int Stock = 0, double Rating = 0, int RatingCount = 0);
public sealed record ProductDetailsDto(Guid Id, Guid PageId, string PageName, string Name, string Description, long Price,
    IReadOnlyList<string?> ImageUrls, int CategoryId, int Stock, bool Available, double Rating = 0, int RatingCount = 0);
public sealed record TopRatedProductDto(Guid Id, Guid PageId, string PageName, double PageRating, int PageRatingCount, string Name, string Description,
    long Price, string? ImageUrl, int Stock, bool Available, double Rating = 0, int RatingCount = 0);

// Favorites
/// <summary>Hidden or switched-off products stay listed with Available = false; deleted ones drop out.</summary>
public sealed record FavoriteProductDto(Guid Id, Guid PageId, string PageName, string Name, string Description, long Price, string? ImageUrl,
    int Stock, bool Available, double Rating, int RatingCount, DateTime FavoritedAt);

// Ratings
public sealed record RateProductDto(int Stars, string? Comment);
public sealed record ProductReviewDto(Guid Id, string CustomerName, int Stars, string Comment, DateTime UpdatedAt);
public sealed record MyProductRatingDto(bool CanRate, int? Stars, string? Comment);
public sealed record ProductRatingSummaryDto(double Rating, int RatingCount, int Stars, string Comment);
public sealed record ReportProductDto(string Reason, string? Text);

// Cart
public sealed record AddCartItemDto(Guid ProductId, int Quantity);
public sealed record SetCartQuantityDto(int Quantity);
public sealed record CartLineDto(Guid ProductId, string Name, string? ImageUrl, long UnitPrice, int Quantity, long LineTotal, bool Available, int Stock);
public sealed record CartDto(Guid? PageId, string? PageName, IReadOnlyList<CartLineDto> Items, long Subtotal);

// Orders
public sealed record CheckoutDto(string Address, int? GovernorateId, string? Note, string PaymentMethod);
public sealed record CheckoutResultDto(Guid OrderId, string No, string Status, string PaymentStatus, long Total, string? PaymentUrl);
public sealed record StoreOrderItemDto(Guid ProductId, string Name, long UnitPrice, int Quantity, string? ImageUrl);
public sealed record StoreOrderDto(Guid Id, string No, Guid PageId, string PageName, string Status, string PaymentMethod, string PaymentStatus,
    string Address, int? GovernorateId, string? Note, long Subtotal, long Commission, string? StatusReason,
    IReadOnlyList<StoreOrderItemDto> Items, DateTime CreatedAt, DateTime StatusChangedAt);
public sealed record SellerOrderDto(StoreOrderDto Order, string? CustomerName, string? CustomerPhone);
public sealed record SetOrderStatusDto(string Status, string? Reason);

// Stats
public sealed record TopProductDto(Guid ProductId, string Name, int Quantity, long Sales);
public sealed record SellerStatsDto(int TotalOrders, IReadOnlyDictionary<string, int> ByStatus, long DeliveredSales, int ActiveProducts, IReadOnlyList<TopProductDto> TopProducts);

// Admin
public sealed record CreateSellerAccountDto(Guid PageId, string Email, string Password);
public sealed record ResetSellerPasswordDto(string Password);
public sealed record SellerAccountDto(Guid Id, Guid PageId, string PageName, string Email, bool Disabled, DateTime? LockedUntil, DateTime? LastLoginAt, DateTime CreatedAt);
public sealed record AdminProductDto(Guid Id, Guid PageId, string PageName, string Name, long Price, int Stock, bool Active, bool HiddenByAdmin, bool Deleted, DateTime CreatedAt);
public sealed record StoreSettingsDto(int MaxProductsPerPage, double CommissionPercent);
public sealed record PageProductLimitDto(int? Limit);
