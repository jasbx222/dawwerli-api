using Dawwerli.Domain.Entities;

namespace Dawwerli.Domain.Rules;

/// <summary>Allowed store-order status moves, per actor.</summary>
public static class StoreOrderFlow
{
    private static readonly Dictionary<StoreOrderStatus, StoreOrderStatus[]> Seller = new()
    {
        [StoreOrderStatus.New] = [StoreOrderStatus.Accepted, StoreOrderStatus.Rejected],
        [StoreOrderStatus.Accepted] = [StoreOrderStatus.OutForDelivery, StoreOrderStatus.Cancelled],
        [StoreOrderStatus.OutForDelivery] = [StoreOrderStatus.Delivered, StoreOrderStatus.Cancelled],
    };

    public static bool SellerCanMove(StoreOrderStatus from, StoreOrderStatus to) =>
        Seller.TryGetValue(from, out var next) && next.Contains(to);

    public static bool CustomerCanCancel(StoreOrderStatus status) => status == StoreOrderStatus.New;

    /// <summary>Reserved stock goes back when the order ends without delivery.</summary>
    public static bool RestoresStock(StoreOrderStatus status) => status is StoreOrderStatus.Cancelled or StoreOrderStatus.Rejected;

    public static long CommissionOf(long subtotal, double percent) => (long)Math.Round(subtotal * percent / 100d, MidpointRounding.AwayFromZero);
}
