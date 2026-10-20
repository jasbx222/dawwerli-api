using Dawwerli.Domain.Common;

namespace Dawwerli.Domain.Entities;

public class Offer : Entity
{
    public Guid RequestId { get; set; }
    public Request? Request { get; set; }
    public Guid PageId { get; set; }
    public Page? Page { get; set; }
    public long Price { get; set; }
    public int DeliveryHours { get; set; }
    public ItemCondition Condition { get; set; }
    public string Note { get; set; } = "";
    public string PhotoKey { get; set; } = "";
    public string PhotoStamp { get; set; } = "";
    public OfferStatus Status { get; set; }
    public bool Sponsored { get; set; }
    public bool Early { get; set; }
    public bool Hidden { get; set; }
}

public class Order : Entity
{
    public long Number { get; set; }
    public Guid RequestId { get; set; }
    public Request? Request { get; set; }
    public Guid OfferId { get; set; }
    public Offer? Offer { get; set; }
    public Guid PageId { get; set; }
    public Page? Page { get; set; }
    public Guid CustomerId { get; set; }
    public OrderStatus Status { get; set; }
    public DateTime SelectedAt { get; set; }
    public DateTime? ContactedAt { get; set; }
    public DateTime? GhostReportedAt { get; set; }
    public DeliveryAnswer? Answer { get; set; }
    public DateTime? AnsweredAt { get; set; }
    public DateTime? DeliveryAskedAt { get; set; }

    public bool PhoneVisible => Status is OrderStatus.InPurchase or OrderStatus.AwaitingDelivery;
}

public class Rating : Entity
{
    public Guid OrderId { get; set; }
    public Guid PageId { get; set; }
    public int Stars { get; set; }
    public string[] Tags { get; set; } = [];
    public string Comment { get; set; } = "";
}
