using Dawwerli.Domain.Entities;
using Dawwerli.Domain.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;

namespace Dawwerli.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<OtpChallenge> OtpChallenges { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<Device> Devices { get; }
    DbSet<Governorate> Governorates { get; }
    DbSet<GovernorateAdjacency> GovernorateAdjacency { get; }
    DbSet<Category> Categories { get; }
    DbSet<BannedKeyword> BannedKeywords { get; }
    DbSet<Page> Pages { get; }
    DbSet<PageCategory> PageCategories { get; }
    DbSet<PageGovernorate> PageGovernorates { get; }
    DbSet<PageMark> PageMarks { get; }
    DbSet<Request> Requests { get; }
    DbSet<RequestDispatch> RequestDispatches { get; }
    DbSet<Offer> Offers { get; }
    DbSet<Order> Orders { get; }
    DbSet<Rating> Ratings { get; }
    DbSet<Report> Reports { get; }
    DbSet<Block> Blocks { get; }
    DbSet<Strike> Strikes { get; }
    DbSet<Dispute> Disputes { get; }
    DbSet<LedgerEntry> LedgerEntries { get; }
    DbSet<TopUp> TopUps { get; }
    DbSet<AdCampaign> AdCampaigns { get; }
    DbSet<AdEvent> AdEvents { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<IdempotencyRecord> IdempotencyRecords { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<SettingsRecord> Settings { get; }
    DbSet<SupportMessage> SupportMessages { get; }
    DbSet<Product> Products { get; }
    DbSet<CartItem> CartItems { get; }
    DbSet<ProductFavorite> ProductFavorites { get; }
    DbSet<ProductRating> ProductRatings { get; }
    DbSet<PageFollow> PageFollows { get; }
    DbSet<Courier> Couriers { get; }
    DbSet<CourierTracking> CourierTrackings { get; }
    DbSet<StoreOrder> StoreOrders { get; }
    DbSet<StoreOrderItem> StoreOrderItems { get; }
    DbSet<StoreOrderEvent> StoreOrderEvents { get; }
    DbSet<SellerAccount> SellerAccounts { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
    EntityEntry<T> Entry<T>(T entity) where T : class;
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default);
    /// <summary>SELECT ... FOR UPDATE on the request row (call inside a transaction).</summary>
    Task LockRequestAsync(Guid requestId, CancellationToken ct = default);
    /// <summary>SELECT ... FOR UPDATE on the store order row, so status moves (and stock restores) run one at a time.</summary>
    Task LockStoreOrderAsync(Guid orderId, CancellationToken ct = default);
    /// <summary>SELECT ... FOR UPDATE on the page row, e.g. to serialize product creation against the page's limit.</summary>
    Task LockPageAsync(Guid pageId, CancellationToken ct = default);
    /// <summary>Recomputes a product's rating average and count from its ratings in one statement.</summary>
    Task RefreshProductRatingAsync(Guid productId, CancellationToken ct = default);
    Task<long> NextNumberAsync(string sequence, CancellationToken ct = default);
}

public interface IClock { DateTime UtcNow { get; } }

public interface ICurrentUser
{
    Guid UserId { get; }
    UserRole Role { get; }
    string? DeviceKey { get; }
}

public interface IPhoneProtector
{
    string Hash(string phone);
    string Encrypt(string phone);
    string Decrypt(string cipher);
}

public interface ISmsSender
{
    /// <returns>false when the gateway failed to deliver.</returns>
    Task<bool> SendOtpAsync(string phone, string code, OtpChannel channel, CancellationToken ct = default);
}

public sealed class AdminAuthOptions
{
    public const string Section = "AdminAuth";
    /// <summary>When false, admins sign in with email + password only.</summary>
    public bool TotpEnabled { get; set; }
    public string TotpIssuer { get; set; } = "";
    public int TotpDriftSteps { get; set; }
    public int MinPasswordLength { get; set; }
}

public sealed class AuditPagingOptions
{
    public const string Section = "AuditPaging";
    public int DefaultPageSize { get; set; }
    public int MaxPageSize { get; set; }
}

public interface ITotpService
{
    /// <summary>New random secret, returned base32 (for the authenticator) and protected (for storage).</summary>
    (string Base32, string Protected) NewSecret();
    string Reveal(string protectedSecret);
    string OtpAuthUri(string account, string base32Secret);
    /// <summary>Returns the matched time step, or null when the code is wrong.</summary>
    long? Verify(string protectedSecret, string code, long lastStep);
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

public sealed record IssuedTokens(string AccessToken, string RefreshToken, int ExpiresIn);

public interface ITokenService
{
    string CreateAccessToken(User user, Guid? pageId);
    int AccessTokenSeconds { get; }
    int RefreshTokenDays { get; }
    string NewOpaqueToken();
    string HashToken(string token);
}

public interface IStorage
{
    Task<string> SaveAsync(Stream content, string extension, string folder, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);
    string? Url(string? key);
}

public interface IRulesProvider
{
    Task<BusinessRules> GetAsync(CancellationToken ct = default);
    Task UpdateAsync(BusinessRules rules, CancellationToken ct = default);
}

/// <summary>Stores the in-app notification and pushes it (FCM / realtime).</summary>
public interface INotifier
{
    Task NotifyAsync(Guid userId, string title, string body, string route, CancellationToken ct = default);
}

public interface IPushSender
{
    Task SendAsync(IReadOnlyCollection<string> tokens, string title, string body, string route, CancellationToken ct = default);
}

/// <summary>Builds the public courier link (/c/{token}) on the API's own host.</summary>
public interface ITrackingLinks
{
    string Url(string token);
}

public interface IRealtime
{
    Task RequestChangedAsync(Guid requestId, CancellationToken ct = default);
    Task PageInboxChangedAsync(Guid pageId, CancellationToken ct = default);
    /// <summary>Pushes the courier's latest position to everyone watching order:{orderId} (its customer and page).</summary>
    Task CourierMovedAsync(Guid orderId, object location, CancellationToken ct = default);
}

public interface ITextCatalog
{
    /// <summary>Localized notification/step text by key (from configuration).</summary>
    string Get(string key, params object[] args);
}

public sealed record PaymentSession(string Reference, string CheckoutUrl);

/// <summary>Electronic payment gateway. <see cref="IsConfigured"/> false = only cash on delivery.</summary>
public interface IPaymentProvider
{
    bool IsConfigured { get; }
    Task<PaymentSession> StartAsync(StoreOrder order, long amount, CancellationToken ct = default);
}

/// <summary>Short-lived JWT for the web seller portal (role SellerPortal, scope seller_portal, page id).</summary>
public interface ISellerTokenService
{
    string Create(SellerAccount account);
    int AccessTokenSeconds { get; }
}

/// <summary>The signed-in web seller (SellerPortal token).</summary>
public interface ICurrentSeller
{
    Guid AccountId { get; }
    Guid PageId { get; }
    /// <summary>Password stamp the token was issued with (see <see cref="Domain.Entities.SellerAccount.StampOf"/>).</summary>
    string? Stamp { get; }
}
