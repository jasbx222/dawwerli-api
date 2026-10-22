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

/// <summary>A customer's 1–5 star rating of a product they received; one per customer per product, editable.</summary>
public class ProductRating : Entity
{
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public int Stars { get; set; }
    public string Comment { get; set; } = "";
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
