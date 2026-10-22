using Dawwerli.Application.Abstractions;
using Dawwerli.Domain.Common;
using Dawwerli.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Dawwerli.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options, IClock clock) : DbContext(options), IAppDbContext
{
    public const string RequestNumberSeq = "request_number";
    public const string OrderNumberSeq = "order_number";

    public DbSet<User> Users => Set<User>();
    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<Governorate> Governorates => Set<Governorate>();
    public DbSet<GovernorateAdjacency> GovernorateAdjacency => Set<GovernorateAdjacency>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<BannedKeyword> BannedKeywords => Set<BannedKeyword>();
    public DbSet<Page> Pages => Set<Page>();
    public DbSet<PageCategory> PageCategories => Set<PageCategory>();
    public DbSet<PageGovernorate> PageGovernorates => Set<PageGovernorate>();
    public DbSet<PageMark> PageMarks => Set<PageMark>();
    public DbSet<Request> Requests => Set<Request>();
    public DbSet<RequestDispatch> RequestDispatches => Set<RequestDispatch>();
    public DbSet<Offer> Offers => Set<Offer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Rating> Ratings => Set<Rating>();
    public DbSet<Report> Reports => Set<Report>();
    public DbSet<Block> Blocks => Set<Block>();
    public DbSet<Strike> Strikes => Set<Strike>();
    public DbSet<Dispute> Disputes => Set<Dispute>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<TopUp> TopUps => Set<TopUp>();
    public DbSet<AdCampaign> AdCampaigns => Set<AdCampaign>();
    public DbSet<AdEvent> AdEvents => Set<AdEvent>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<SettingsRecord> Settings => Set<SettingsRecord>();
    public DbSet<SupportMessage> SupportMessages => Set<SupportMessage>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<CartItem> CartItems => Set<CartItem>();
    public DbSet<ProductFavorite> ProductFavorites => Set<ProductFavorite>();
    public DbSet<ProductRating> ProductRatings => Set<ProductRating>();
    public DbSet<StoreOrder> StoreOrders => Set<StoreOrder>();
    public DbSet<StoreOrderItem> StoreOrderItems => Set<StoreOrderItem>();
    public DbSet<StoreOrderEvent> StoreOrderEvents => Set<StoreOrderEvent>();
    public DbSet<SellerAccount> SellerAccounts => Set<SellerAccount>();

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default) =>
        Database.CurrentTransaction is null ? Database.BeginTransactionAsync(ct) : Task.FromResult<IDbContextTransaction>(new NoopTransaction());

    public Task LockRequestAsync(Guid requestId, CancellationToken ct = default) =>
        Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM requests WHERE id = {requestId} FOR UPDATE", ct);

    public Task LockStoreOrderAsync(Guid orderId, CancellationToken ct = default) =>
        Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM store_orders WHERE id = {orderId} FOR UPDATE", ct);

    public Task RefreshProductRatingAsync(Guid productId, CancellationToken ct = default) =>
        Database.ExecuteSqlInterpolatedAsync($@"UPDATE products SET
            rating = COALESCE((SELECT round(avg(stars)::numeric, 1) FROM product_ratings WHERE product_id = {productId}), 0)::double precision,
            rating_count = (SELECT count(*) FROM product_ratings WHERE product_id = {productId})
            WHERE id = {productId}", ct);

    public Task LockPageAsync(Guid pageId, CancellationToken ct = default) =>
        Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM pages WHERE id = {pageId} FOR UPDATE", ct);

    public async Task<long> NextNumberAsync(string sequence, CancellationToken ct = default) =>
        (await Database.SqlQueryRaw<long>($"SELECT nextval('{(sequence == OrderNumberSeq ? OrderNumberSeq : RequestNumberSeq)}') AS \"Value\"").ToListAsync(ct))[0];

    public override Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;
        foreach (var e in ChangeTracker.Entries<Entity>())
        {
            if (e.State == EntityState.Added && e.Entity.CreatedAt == default) e.Entity.CreatedAt = now;
            if (e.State is EntityState.Added or EntityState.Modified) e.Entity.UpdatedAt = now;
        }
        return base.SaveChangesAsync(ct);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasSequence<long>(RequestNumberSeq).StartsAt(1000);
        b.HasSequence<long>(OrderNumberSeq).StartsAt(20000);
        b.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }

    /// <summary>Lets services open a "transaction" when the caller already owns one.</summary>
    private sealed class NoopTransaction : IDbContextTransaction
    {
        public Guid TransactionId { get; } = Guid.NewGuid();
        public void Commit() { }
        public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Rollback() { }
        public Task RollbackAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
