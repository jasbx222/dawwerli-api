using Dawwerli.Domain.Common;

namespace Dawwerli.Domain.Entities;

public class AdCampaign : Entity
{
    public AdOwnerType OwnerType { get; set; }
    public Guid? PageId { get; set; }
    public Page? Page { get; set; }
    public AdSlot Slot { get; set; }
    public string Title { get; set; } = "";
    public string Headline { get; set; } = "";
    public int DiscountPercent { get; set; }
    public int[] GovernorateIds { get; set; } = [];
    public int[] CategoryIds { get; set; } = [];
    public DateTime StartAt { get; set; }
    public DateTime EndAt { get; set; }
    public long DailyPrice { get; set; }
    public DateTime? LastChargedOn { get; set; }
    public AdStatus Status { get; set; }
    public string? ImageKey { get; set; }
    public string? DeepLink { get; set; }
    public string? Note { get; set; }
    public long Impressions { get; set; }
    public long Clicks { get; set; }
    public long Selections { get; set; }
}

public class AdEvent
{
    public long Id { get; set; }
    public Guid CampaignId { get; set; }
    public AdEventType Type { get; set; }
    public string DeviceKey { get; set; } = "";
    /// <summary>UTC hour bucket for per-device-per-hour dedup.</summary>
    public DateTime Hour { get; set; }
}
