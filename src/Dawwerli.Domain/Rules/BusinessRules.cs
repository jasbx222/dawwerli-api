namespace Dawwerli.Domain.Rules;

/// <summary>
/// Every limit, timer and price. Values come from configuration ("Business" section)
/// on first run and then from the admin-editable settings row; nothing here has a default.
/// </summary>
public sealed class BusinessRules
{
    public int MaxOffers { get; set; }
    public int MaxEarlySlots { get; set; }
    public int EarlyWindowSec { get; set; }
    public int MaxOpenRequests { get; set; }
    public int MaxVoiceSeconds { get; set; }
    public int MinWordsWithoutMedia { get; set; }
    public int ExpandAfterMin { get; set; }
    public int ExpireAfterHours { get; set; }
    public int ReplyWindowMin { get; set; }
    public int GhostAfterHours { get; set; }
    public int DeliveryReaskDays { get; set; }
    public int DeliveryCloseDays { get; set; }
    public int OfferRecoveryHours { get; set; }
    public int OtpLength { get; set; }
    public int OtpExpirySec { get; set; }
    public int OtpResendSec { get; set; }
    public int OtpMaxWrong { get; set; }
    public int OtpLockMin { get; set; }
    public int OtpSmsFailuresBeforeWhatsApp { get; set; }
    public int NoReplyMarksForSuspension { get; set; }
    public int MarksWindowDays { get; set; }
    public int SuspensionDays { get; set; }
    public int StrikesForBan { get; set; }
    public int StrikeWindowDays { get; set; }
    public int PostingBanDays { get; set; }
    public int BansForPermanent { get; set; }
    public int ComplaintsForSuspension { get; set; }
    public int ComplaintsForBan { get; set; }
    public int DisputeDecisionHours { get; set; }
    public int TrustedDeals { get; set; }
    public bool BillingEnabled { get; set; }
    public long SelectionFee { get; set; }
    public int FreeSelections { get; set; }
    public long EarlyAccessMonthly { get; set; }
    public int EarlyAccessDays { get; set; }
    public long BannerDaily { get; set; }
    public long FeaturedWeekly { get; set; }
    public long SponsoredOffer { get; set; }
    public double MinAdRating { get; set; }
    public long MinTopUp { get; set; }
    public int FeedDelayMin { get; set; }
    public int StatsMinVerifiedPages { get; set; }
    public int StatsMinGovernorates { get; set; }
    public int StatsMinSamples { get; set; }
    public int HomeListSize { get; set; }
    /// <summary>Products a page may list; 0 = use Store:DefaultMaxProductsPerPage.</summary>
    public int MaxProductsPerPage { get; set; }
    /// <summary>Optional platform commission on store orders (0 = none).</summary>
    public double StoreCommissionPercent { get; set; }
    public string MinAppVersion { get; set; } = "";
    public string[] TopUpMethods { get; set; } = [];
    public string[] RatingTags { get; set; } = [];
    public string[] LowRatingTags { get; set; } = [];
}
