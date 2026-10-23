using Dawwerli.Domain.Common;

namespace Dawwerli.Domain.Entities;

/// <summary>Item a page sells; shown on the page profile and in the home top-rated strip, never in the request feed.</summary>
public class Product : Entity
{
    public Guid PageId { get; set; }
    public Page? Page { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    /// <summary>IQD.</summary>
    public long Price { get; set; }
    public string[] ImageKeys { get; set; } = [];
    public int CategoryId { get; set; }
    public int Stock { get; set; }
    /// <summary>Seller switch: false = hidden from customers.</summary>
    public bool Active { get; set; } = true;
    public bool HiddenByAdmin { get; set; }
    public DateTime? DeletedAt { get; set; }
    public uint Version { get; set; }
    /// <summary>Average of <see cref="ProductRating.Stars"/>, kept in step on every rating change.</summary>
    public double Rating { get; set; }
    public int RatingCount { get; set; }

    /// <summary>Physical size in cm (one decimal). Width and height come together; depth is optional.</summary>
    public decimal? WidthCm { get; set; }
    public decimal? HeightCm { get; set; }
    public decimal? DepthCm { get; set; }
    /// <summary>Set only for wall-placeable products (needs width and height).</summary>
    public WallKind? WallKind { get; set; }
    /// <summary>
    /// Page-local key linking variants of one product (e.g. 43/55/65 inch). Siblings are looked up inside the same page only,
    /// so another page using the same key never joins the group.
    /// </summary>
    public string? VariantGroup { get; set; }
    /// <summary>Short switcher label for this variant (e.g. "55 بوصة"); required when <see cref="VariantGroup"/> is set.</summary>
    public string? VariantLabel { get; set; }

    public bool HasWallSize => WidthCm is not null && HeightCm is not null;
    public bool WallPlaceable => HasWallSize && WallKind is not null;
    public bool Visible => Active && !HiddenByAdmin && DeletedAt == null;
    public bool Available => Visible && Stock > 0;
}

/// <summary>A product the customer hearted.</summary>
public class ProductFavorite : Entity
{
    public Guid UserId { get; set; }
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
}

/// <summary>A customer's 1–5 star rating of a product they received; one per customer per product (the owner may edit it).</summary>
public class ProductRating : Entity
{
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public int Stars { get; set; }
    public string Comment { get; set; } = "";
    /// <summary>Hidden by an admin (abuse): left out of the reviews list and the product average.</summary>
    public bool HiddenByAdmin { get; set; }
}

/// <summary>One line of a customer's cart. A cart only ever holds products of one page.</summary>
public class CartItem : Entity
{
    public Guid UserId { get; set; }
    public Guid PageId { get; set; }
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public int Quantity { get; set; }
}

public class StoreOrder : Entity
{
    public long Number { get; set; }
    public Guid CustomerUserId { get; set; }
    public Guid PageId { get; set; }
    public Page? Page { get; set; }
    public StoreOrderStatus Status { get; set; }
    public StoreOrderKind Kind { get; set; }
    /// <summary>Offer orders: the request and the trust-side order (inspect, mismatch, dispute, rating) this order tracks.</summary>
    public Guid? RequestId { get; set; }
    public Guid? OfferOrderId { get; set; }
    /// <summary>Optional delivery pin the customer dropped on the map, with a short note (no live tracking).</summary>
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? LocationNote { get; set; }
    /// <summary>Courier picked by the page when the order went out; name and phone are snapshotted.</summary>
    public Guid? CourierId { get; set; }
    public string? CourierName { get; set; }
    public string? CourierPhone { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public PaymentStatus PaymentStatus { get; set; }
    public string? PaymentReference { get; set; }
    public string Address { get; set; } = "";
    public int? GovernorateId { get; set; }
    public string? Note { get; set; }
    public long Subtotal { get; set; }
    public double CommissionPercent { get; set; }
    public long Commission { get; set; }
    public string? StatusReason { get; set; }
    public DateTime StatusChangedAt { get; set; }
    public List<StoreOrderItem> Items { get; set; } = [];
    /// <summary>Every status the order reached, oldest first (the customer's timeline).</summary>
    public List<StoreOrderEvent> History { get; set; } = [];

    public void Record(StoreOrderStatus status, DateTime at)
    {
        Status = status;
        StatusChangedAt = at;
        History.Add(new StoreOrderEvent { OrderId = Id, Status = status, At = at });
    }
}

/// <summary>A delivery person the page sends orders with. The customer sees the name and phone once the order is out.</summary>
public class Courier : Entity
{
    public Guid PageId { get; set; }
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";
    public bool Active { get; set; } = true;
    public DateTime? DeletedAt { get; set; }
}

/// <summary>One step of a store order's status timeline.</summary>
public class StoreOrderEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public StoreOrderStatus Status { get; set; }
    public DateTime At { get; set; }
}

/// <summary>Snapshot of the product at checkout (name/price can change later).</summary>
public class StoreOrderItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid ProductId { get; set; }
    public string Name { get; set; } = "";
    public long UnitPrice { get; set; }
    public int Quantity { get; set; }
    public string? ImageKey { get; set; }
}

/// <summary>Web seller-portal login, created by an admin and tied to one page.</summary>
public class SellerAccount : Entity
{
    public Guid PageId { get; set; }
    public Page? Page { get; set; }
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool Disabled { get; set; }
    public int FailedLogins { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// Short fingerprint of the password hash, carried in portal tokens. A password reset changes the hash,
    /// so tokens issued before it stop matching and are refused.
    /// </summary>
    public static string StampOf(string passwordHash) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(passwordHash)))[..16];
}

/// <summary>
/// Live-location link the page hands to the courier of one store order (no courier login). The token is 256 random bits,
/// so it is stored as-is and looked up by its unique index. It only works while the order is out for delivery with the same
/// courier; it is revoked when the order ends, the courier is changed or the page issues a new link.
/// Only the latest position is kept and it is wiped when tracking ends.
/// </summary>
public class CourierTracking : Entity
{
    public Guid OrderId { get; set; }
    public Guid? CourierId { get; set; }
    public string Token { get; set; } = "";
    public DateTime? RevokedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? StoppedAt { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? Accuracy { get; set; }
    public double? Heading { get; set; }
    public double? Speed { get; set; }
    public DateTime? LocationAt { get; set; }

    public bool Sharing => RevokedAt == null && StartedAt != null && StoppedAt == null;

    public void ClearLocation()
    {
        Latitude = Longitude = Accuracy = Heading = Speed = null;
        LocationAt = null;
    }
}
