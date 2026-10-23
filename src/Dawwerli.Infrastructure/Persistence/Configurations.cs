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
        b.HasIndex(x => x.FeaturedOrder).IsUnique().HasFilter("featured_order IS NOT NULL");
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
        b.Property(x => x.WidthCm).HasPrecision(5, 1);
        b.Property(x => x.HeightCm).HasPrecision(5, 1);
        b.Property(x => x.DepthCm).HasPrecision(5, 1);
        b.Property(x => x.VariantGroup).HasMaxLength(40);
        b.Property(x => x.VariantLabel).HasMaxLength(30);
        b.HasIndex(x => new { x.PageId, x.VariantGroup }).HasFilter("variant_group IS NOT NULL");
        b.ToTable(t =>
        {
            t.HasCheckConstraint("ck_products_stock", "stock >= 0");
            t.HasCheckConstraint("ck_products_size", "(width_cm IS NULL) = (height_cm IS NULL)"
                + " AND (width_cm IS NULL OR (width_cm BETWEEN 1 AND 1000 AND height_cm BETWEEN 1 AND 1000))"
                + " AND (depth_cm IS NULL OR (width_cm IS NOT NULL AND depth_cm BETWEEN 1 AND 1000))"
                + " AND (wall_kind IS NULL OR width_cm IS NOT NULL)");
        });
    }
}

internal sealed class ProductFavoriteConfig : IEntityTypeConfiguration<ProductFavorite>
{
    public void Configure(EntityTypeBuilder<ProductFavorite> b)
    {
        b.HasIndex(x => new { x.UserId, x.ProductId }).IsUnique();
        b.HasIndex(x => new { x.UserId, x.CreatedAt });
    }
}

internal sealed class ProductRatingConfig : IEntityTypeConfiguration<ProductRating>
{
    public void Configure(EntityTypeBuilder<ProductRating> b)
    {
        b.HasIndex(x => new { x.ProductId, x.UserId }).IsUnique();
        b.HasIndex(x => new { x.ProductId, x.UpdatedAt });
        b.HasIndex(x => new { x.ProductId, x.HiddenByAdmin });
        b.Property(x => x.Comment).HasMaxLength(1000);
        b.ToTable(t => t.HasCheckConstraint("ck_product_ratings_stars", "stars BETWEEN 1 AND 5"));
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
        b.Property(x => x.LocationNote).HasMaxLength(300);
        b.Property(x => x.CourierName).HasMaxLength(80);
        b.Property(x => x.CourierPhone).HasMaxLength(20);
        b.HasIndex(x => x.OfferOrderId).IsUnique().HasFilter("offer_order_id IS NOT NULL");
        b.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.OrderId);
        b.HasMany(x => x.History).WithOne().HasForeignKey(e => e.OrderId);
    }
}

internal sealed class CourierConfig : IEntityTypeConfiguration<Courier>
{
    public void Configure(EntityTypeBuilder<Courier> b)
    {
        b.Property(x => x.Name).HasMaxLength(80);
        b.Property(x => x.Phone).HasMaxLength(20);
        b.HasIndex(x => x.PageId);
    }
}

internal sealed class CourierTrackingConfig : IEntityTypeConfiguration<CourierTracking>
{
    public void Configure(EntityTypeBuilder<CourierTracking> b)
    {
        b.Property(x => x.Token).HasMaxLength(64);
        b.HasIndex(x => x.Token).IsUnique();
        b.HasIndex(x => new { x.OrderId, x.RevokedAt });
        b.HasOne<StoreOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Cascade);
        b.Ignore(x => x.Sharing);
    }
}

internal sealed class StoreOrderEventConfig : IEntityTypeConfiguration<StoreOrderEvent>
{
    public void Configure(EntityTypeBuilder<StoreOrderEvent> b)
    {
        // Ids are set in code; events added to a tracked order must insert, not update.
        b.Property(x => x.Id).ValueGeneratedNever();
        b.HasIndex(x => new { x.OrderId, x.At });
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

internal sealed class PageFollowConfig : IEntityTypeConfiguration<PageFollow>
{
    public void Configure(EntityTypeBuilder<PageFollow> b)
    {
        b.HasIndex(x => new { x.UserId, x.PageId }).IsUnique();
        b.HasIndex(x => x.PageId);
        b.HasOne(x => x.Page).WithMany().HasForeignKey(x => x.PageId).OnDelete(DeleteBehavior.Cascade);
    }
}
