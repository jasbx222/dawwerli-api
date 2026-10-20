using Dawwerli.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Dawwerli.Infrastructure.Persistence;

internal sealed class UserConfig : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.HasIndex(x => x.PhoneHash).IsUnique();
        b.HasIndex(x => x.Email).IsUnique().HasFilter("email IS NOT NULL");
        b.Property(x => x.Name).HasMaxLength(60);
        b.Property(x => x.Area).HasMaxLength(60);
    }
}

internal sealed class OtpConfig : IEntityTypeConfiguration<OtpChallenge>
{
    public void Configure(EntityTypeBuilder<OtpChallenge> b) => b.HasIndex(x => new { x.PhoneHash, x.CreatedAt });
}

internal sealed class RefreshConfig : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => x.FamilyId);
    }
}

internal sealed class DeviceConfig : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> b)
    {
        b.HasIndex(x => x.PushToken).IsUnique();
        b.HasIndex(x => x.UserId);
    }
}

internal sealed class AdjacencyConfig : IEntityTypeConfiguration<GovernorateAdjacency>
{
    public void Configure(EntityTypeBuilder<GovernorateAdjacency> b) => b.HasKey(x => new { x.GovernorateId, x.NeighborId });
}

internal sealed class CategoryConfig : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> b)
    {
        b.HasIndex(x => x.Slug).IsUnique();
        b.Property(x => x.FieldSchema).HasColumnType("jsonb");
    }
}

internal sealed class KeywordConfig : IEntityTypeConfiguration<BannedKeyword>
{
    public void Configure(EntityTypeBuilder<BannedKeyword> b) => b.HasIndex(x => x.Word).IsUnique();
}

internal sealed class PageConfig : IEntityTypeConfiguration<Page>
{
    public void Configure(EntityTypeBuilder<Page> b)
    {
        b.HasIndex(x => x.InstagramHandle).IsUnique();
        b.HasIndex(x => x.UserId).IsUnique();
        b.HasIndex(x => x.Status);
        b.HasMany(x => x.Categories).WithOne().HasForeignKey(x => x.PageId);
        b.HasMany(x => x.Governorates).WithOne().HasForeignKey(x => x.PageId);
    }
}

internal sealed class PageCategoryConfig : IEntityTypeConfiguration<PageCategory>
{
    public void Configure(EntityTypeBuilder<PageCategory> b)
    {
        b.HasKey(x => new { x.PageId, x.CategoryId });
        b.HasIndex(x => new { x.CategoryId, x.PageId });
    }
}

internal sealed class PageGovernorateConfig : IEntityTypeConfiguration<PageGovernorate>
{
    public void Configure(EntityTypeBuilder<PageGovernorate> b)
    {
        b.HasKey(x => new { x.PageId, x.GovernorateId });
        b.HasIndex(x => new { x.GovernorateId, x.PageId });
    }
}

internal sealed class PageMarkConfig : IEntityTypeConfiguration<PageMark>
{
    public void Configure(EntityTypeBuilder<PageMark> b) => b.HasIndex(x => new { x.PageId, x.CreatedAt });
}

internal sealed class RequestConfig : IEntityTypeConfiguration<Request>
{
    public void Configure(EntityTypeBuilder<Request> b)
    {
        b.HasIndex(x => x.Number).IsUnique();
        b.HasIndex(x => new { x.Status, x.CategoryId, x.GovernorateId, x.CreatedAt });
        b.HasIndex(x => x.CustomerId);
        b.Property(x => x.Fields).HasColumnType("jsonb");
        b.HasIndex(x => x.Fields).HasMethod("gin");
        b.Property(x => x.Version).IsRowVersion();
    }
}

internal sealed class DispatchConfig : IEntityTypeConfiguration<RequestDispatch>
{
    public void Configure(EntityTypeBuilder<RequestDispatch> b)
    {
        b.HasKey(x => new { x.RequestId, x.PageId });
        b.HasIndex(x => new { x.PageId, x.DispatchedAt });
    }
}

internal sealed class OfferConfig : IEntityTypeConfiguration<Offer>
{
    public void Configure(EntityTypeBuilder<Offer> b)
    {
        b.HasIndex(x => new { x.RequestId, x.PageId }).IsUnique();
        b.HasIndex(x => new { x.PageId, x.Status });
        b.Property(x => x.Note).HasMaxLength(300);
    }
}

internal sealed class OrderConfig : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.HasIndex(x => x.Number).IsUnique();
        b.HasIndex(x => new { x.PageId, x.Status });
        b.HasIndex(x => x.RequestId);
        b.HasIndex(x => x.OfferId);
    }
}

internal sealed class RatingConfig : IEntityTypeConfiguration<Rating>
{
    public void Configure(EntityTypeBuilder<Rating> b)
    {
        b.HasIndex(x => x.OrderId).IsUnique();
        b.HasIndex(x => x.PageId);
    }
}

internal sealed class BlockConfig : IEntityTypeConfiguration<Block>
{
    public void Configure(EntityTypeBuilder<Block> b) => b.HasIndex(x => new { x.BlockerUserId, x.BlockedUserId }).IsUnique();
}

internal sealed class StrikeConfig : IEntityTypeConfiguration<Strike>
{
    public void Configure(EntityTypeBuilder<Strike> b) => b.HasIndex(x => new { x.UserId, x.CreatedAt });
}

internal sealed class LedgerConfig : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> b)
    {
        b.HasIndex(x => new { x.PageId, x.CreatedAt });
        b.HasIndex(x => new { x.OrderId, x.Type }).IsUnique().HasFilter("order_id IS NOT NULL");
    }
}

internal sealed class TopUpConfig : IEntityTypeConfiguration<TopUp>
{
    public void Configure(EntityTypeBuilder<TopUp> b) => b.HasIndex(x => x.TransactionRef).IsUnique();
}

internal sealed class AdConfig : IEntityTypeConfiguration<AdCampaign>
{
    public void Configure(EntityTypeBuilder<AdCampaign> b) => b.HasIndex(x => new { x.Slot, x.Status, x.StartAt, x.EndAt });
}

internal sealed class AdEventConfig : IEntityTypeConfiguration<AdEvent>
{
    public void Configure(EntityTypeBuilder<AdEvent> b) => b.HasIndex(x => new { x.CampaignId, x.Type, x.DeviceKey, x.Hour }).IsUnique();
}

internal sealed class NotificationConfig : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b) => b.HasIndex(x => new { x.UserId, x.CreatedAt });
}

internal sealed class IdempotencyConfig : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> b) => b.HasKey(x => new { x.UserId, x.Key });
}

internal sealed class SettingsConfig : IEntityTypeConfiguration<SettingsRecord>
{
    public void Configure(EntityTypeBuilder<SettingsRecord> b)
    {
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.Data).HasColumnType("jsonb");
    }
}

internal sealed class AuditConfig : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.Property(x => x.Data).HasColumnType("jsonb");
        b.HasIndex(x => x.CreatedAt);
    }
}

internal sealed class ProductConfig : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.HasIndex(x => new { x.PageId, x.CreatedAt });
        b.Property(x => x.Name).HasMaxLength(120);
        b.Property(x => x.Description).HasMaxLength(4000);
        b.Property(x => x.Version).IsRowVersion();
        b.ToTable(t => t.HasCheckConstraint("ck_products_stock", "stock >= 0"));
    }
}

internal sealed class CartItemConfig : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> b) => b.HasIndex(x => new { x.UserId, x.ProductId }).IsUnique();
}

internal sealed class StoreOrderConfig : IEntityTypeConfiguration<StoreOrder>
{
    public void Configure(EntityTypeBuilder<StoreOrder> b)
    {
        b.HasIndex(x => x.Number).IsUnique();
        b.HasIndex(x => new { x.PageId, x.Status, x.CreatedAt });
        b.HasIndex(x => new { x.CustomerUserId, x.CreatedAt });
        b.Property(x => x.Address).HasMaxLength(500);
        b.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.OrderId);
    }
}

internal sealed class SellerAccountConfig : IEntityTypeConfiguration<SellerAccount>
{
    public void Configure(EntityTypeBuilder<SellerAccount> b)
    {
        b.HasIndex(x => x.Email).IsUnique();
        b.HasIndex(x => x.PageId);
        b.Property(x => x.Email).HasMaxLength(254);
    }
}
