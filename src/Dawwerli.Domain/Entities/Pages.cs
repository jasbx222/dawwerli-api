using Dawwerli.Domain.Common;

namespace Dawwerli.Domain.Entities;

public class Page : Entity
{
    public Guid UserId { get; set; }
    public User? User { get; set; }
    public string Name { get; set; } = "";
    public string InstagramHandle { get; set; } = "";
    public string? Bio { get; set; }
    public string? ImageKey { get; set; }
    public PageStatus Status { get; set; }
    public string? RejectReason { get; set; }
    public string BioCode { get; set; } = "";
    public DateTime? SuspendedUntil { get; set; }
    public double Rating { get; set; }
    public int RatingCount { get; set; }
    public int Deals { get; set; }
    public int FreeSelectionsUsed { get; set; }
    public long Balance { get; set; }
    public bool Receiving { get; set; } = true;
    public bool Sound { get; set; } = true;
    public int WorkFrom { get; set; }
    public int WorkTo { get; set; }
    public int[] WorkDays { get; set; } = [];
    public DateTime? EarlyAccessUntil { get; set; }
    public List<PageCategory> Categories { get; set; } = [];
    public List<PageGovernorate> Governorates { get; set; } = [];

    public bool HasEarlyAccess(DateTime now) => EarlyAccessUntil > now;
    public bool IsActive(DateTime now) => Status == PageStatus.Verified && !(SuspendedUntil > now);
}

public class PageCategory
{
    public Guid PageId { get; set; }
    public int CategoryId { get; set; }
}

public class PageGovernorate
{
    public Guid PageId { get; set; }
    public int GovernorateId { get; set; }
}

public class PageMark : Entity
{
    public Guid PageId { get; set; }
    public PageMarkKind Kind { get; set; }
    public Guid? OrderId { get; set; }
    public string Title { get; set; } = "";
    public bool UnderReview { get; set; }
}
