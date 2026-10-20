using Dawwerli.Domain.Common;

namespace Dawwerli.Domain.Entities;

public class Request : Entity
{
    public Guid CustomerId { get; set; }
    public User? Customer { get; set; }
    public long Number { get; set; }
    public string Description { get; set; } = "";
    public InputMode Mode { get; set; }
    public string? MediaKey { get; set; }
    public int? VoiceSeconds { get; set; }
    public int CategoryId { get; set; }
    public Category? Category { get; set; }
    public string Fields { get; set; } = "{}";
    public ItemCondition Condition { get; set; }
    public long MaxPrice { get; set; }
    public int GovernorateId { get; set; }
    public Governorate? Governorate { get; set; }
    public RequestStatus Status { get; set; }
    public int OffersCount { get; set; }
    public int EarlyOffersCount { get; set; }
    public DateTime EarlyUntil { get; set; }
    public bool PublicDispatched { get; set; }
    public DateTime ExpandsAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public int[] WidenedTo { get; set; } = [];
    public Guid? SponsoredOfferId { get; set; }
    public uint Version { get; set; }

    public bool AcceptsOffers => Status is RequestStatus.Open or RequestStatus.Expanded;
    public bool IsLive => Status is RequestStatus.Open or RequestStatus.Expanded or RequestStatus.Locked;
    public string Title => Description.Length > 60 ? Description[..60] : Description;
}

public class RequestDispatch
{
    public Guid RequestId { get; set; }
    public Guid PageId { get; set; }
    public DateTime DispatchedAt { get; set; }
    public DateTime? SeenAt { get; set; }
}
