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
    Task<int> SaveChangesAsync(CancellationToken ct = default);
    EntityEntry<T> Entry<T>(T entity) where T : class;
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default);
    /// <summary>SELECT ... FOR UPDATE on the request row (call inside a transaction).</summary>
    Task LockRequestAsync(Guid requestId, CancellationToken ct = default);
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

public interface IRealtime
{
    Task RequestChangedAsync(Guid requestId, CancellationToken ct = default);
    Task PageInboxChangedAsync(Guid pageId, CancellationToken ct = default);
}

public interface ITextCatalog
{
    /// <summary>Localized notification/step text by key (from configuration).</summary>
    string Get(string key, params object[] args);
}
