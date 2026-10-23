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
    IReadOnlyList<string?> ImageUrls, int CategoryId, int Stock, bool Available, double Rating = 0, int RatingCount = 0,
    bool HasRated = false, ProductReviewDto? MyReview = null, bool IsFavorite = false);
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
public sealed record CheckoutDto(string Address, int? GovernorateId, string? Note, string PaymentMethod,
    double? Latitude = null, double? Longitude = null, string? LocationNote = null);
/// <summary>Optional map pin the customer drops for delivery (no live tracking).</summary>
public sealed record OrderPinDto(double Latitude, double Longitude, string? Note);
/// <summary>Courier on an order: shown to the customer once the page sends the order out.</summary>
public sealed record OrderCourierDto(string Name, string Phone);
public sealed record CourierDto(Guid Id, string Name, string Phone, bool Active);
public sealed record UpsertCourierDto(string Name, string Phone, bool Active = true);
public sealed record CheckoutResultDto(Guid OrderId, string No, string Status, string PaymentStatus, long Total, string? PaymentUrl);
public sealed record StoreOrderItemDto(Guid ProductId, string Name, long UnitPrice, int Quantity, string? ImageUrl);
public sealed record StoreOrderDto(Guid Id, string No, Guid PageId, string PageName, string Status, string PaymentMethod, string PaymentStatus,
    string Address, int? GovernorateId, string? Note, long Subtotal, long Commission, string? StatusReason,
    IReadOnlyList<StoreOrderItemDto> Items, DateTime CreatedAt, DateTime StatusChangedAt, IReadOnlyList<StoreOrderEventDto> History,
    string Kind = "store", Guid? RequestId = null, Guid? OfferOrderId = null, OrderPinDto? Location = null, OrderCourierDto? Courier = null,
    string? DealStatus = null, string? Answer = null, bool CanAnswer = false, bool CanRate = false);
public sealed record StoreOrderEventDto(string Status, DateTime At);
public sealed record SellerOrderDto(StoreOrderDto Order, string? CustomerName, string? CustomerPhone,
    string? CourierTrackingUrl = null, CourierTrackingLinkDto? CourierTracking = null);
public sealed record SetOrderStatusDto(string Status, string? Reason, Guid? CourierId = null);

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

// Customer discovery: home strips, pages directory, page profile, follows
public sealed record DiscoverProductDto(Guid Id, Guid PageId, string PageName, string Name, string Description, long Price, string? ImageUrl,
    int Stock, bool Available, double Rating, int RatingCount, int Sold);
public sealed record PageCardDto(Guid Id, string Name, string? Image, string? Bio, double Rating, int RatingCount, int Deals, bool Trusted,
    int ProductCount, int FollowerCount, bool IsFollowing, IReadOnlyList<string> Categories, IReadOnlyList<string?> Previews, int? FeaturedOrder);
public sealed record PageProfileDto(Guid Id, string Name, string? Image, string? Bio, double Rating, int RatingCount, int Deals, bool Trusted,
    IReadOnlyList<string> Categories, IReadOnlyList<string> Governorates, int MemberSince,
    int ProductCount, int FollowerCount, int OrderCount, bool IsFollowing);
public sealed record FollowStateDto(Guid PageId, bool IsFollowing, int FollowerCount);
public sealed record SetFeaturedOrderDto(int? Order);
public sealed record ReorderFeaturedDto(IReadOnlyList<Guid> PageIds);
public sealed record AdminReviewDto(Guid Id, Guid ProductId, string ProductName, Guid PageId, string PageName, string CustomerName,
    int Stars, string Comment, bool HiddenByAdmin, DateTime UpdatedAt);
public sealed record AdminFeaturedPageDto(Guid Id, string Name, string? Image, int Order, double Rating, int FollowerCount);

// Courier live tracking (courier link, no courier login)
/// <summary>What the courier sees on the tracking link. Customer name and phone are returned only while the link is valid.</summary>
public sealed record TrackItemDto(string Name, int Quantity);
public sealed record TrackOrderDto(string No, string Status, string PageName, string? PagePhone, string? CustomerName, string? CustomerPhone,
    string CourierName, string Address, string? Note, OrderPinDto? Location, IReadOnlyList<TrackItemDto> Items,
    string PaymentMethod, long CollectAmount, bool Sharing, DateTime? LastSentAt);
public sealed record TrackLocationDto(double Lat, double Lng, double? Accuracy = null, double? Heading = null, double? Speed = null);
public sealed record TrackStateDto(bool Sharing, DateTime? LastSentAt);
/// <summary>Courier position for the order's customer or page. Distance is a straight line and the ETA a rough estimate.</summary>
public sealed record CourierLocationDto(bool Active, double? Latitude = null, double? Longitude = null, double? Accuracy = null, double? Heading = null,
    DateTime? UpdatedAt = null, double? DistanceMeters = null, int? EtaMinutes = null, bool Approximate = true);
public sealed record CourierTrackingLinkDto(string Url, bool Sharing, DateTime? LastLocationAt, DateTime CreatedAt);
public sealed record AssignCourierDto(Guid CourierId);
