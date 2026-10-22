namespace Dawwerli.Application.Contracts;

// ---- auth / account
public sealed record OtpRequestDto(string Phone, bool SmsNotReceived = false);
public sealed record OtpSentDto(string Channel, int ExpiresIn, int ResendIn, string? DevCode);
public sealed record CodeDto(string Code);
public sealed record OtpVerifyDto(string Phone, string Code, string Role, string? DeviceId);
public sealed record AuthResultDto(string AccessToken, string RefreshToken, int ExpiresIn, bool NeedsProfile, string Role, string? PageStatus, bool HasPassword = false);
public sealed record PasswordLoginDto(string Phone, string Password, string Role, string? DeviceId);
public sealed record SetPasswordDto(string Password);
public sealed record PasswordResetDto(string Phone, string Code, string Password, string Role, string? DeviceId);
public sealed record SwitchRoleDto(string Role, string? DeviceId);
public sealed record RefreshDto(string RefreshToken, string? DeviceId);
public sealed record AdminLoginDto(string Email, string Password, string? Code = null);
/// <summary>Status: ok (Auth set) | enroll (scan Secret/OtpAuthUri, then log in again with Code) | code_required.</summary>
public sealed record AdminLoginResultDto(string Status, AuthResultDto? Auth, string? Secret = null, string? OtpAuthUri = null);
public sealed record ProfileDto(string Name, int GovernorateId, string Area);
public sealed record MeDto(Guid Id, string Role, string? Name, int? GovernorateId, string? Governorate, string? Area, string Phone, Guid? PageId, string? PageStatus);
public sealed record StrikeDto(Guid Id, string Reason, DateTime At, bool Frozen);
public sealed record StandingDto(IReadOnlyList<StrikeDto> Strikes, int StrikesForBan, DateTime? BannedUntil, bool Permanent);
public sealed record DeviceDto(string PushToken, string Platform, string? AppVersion);

// ---- catalog
public sealed record CategorySchemaDto(string Hint, IReadOnlyList<string> Required, IReadOnlyList<string> Sizes, IReadOnlyList<string> Colors, IReadOnlyList<long> PriceSuggestions);
public sealed record CategoryDto(int Id, string Slug, string Name, string? Emoji, string? Image, string Badge, CategorySchemaDto Schema);
public sealed record GovernorateDto(int Id, string Name, IReadOnlyList<int> Neighbors);
public sealed record SuggestDto(string? Text);
public sealed record MediaDto(string Key, string Url);

// ---- requests
public sealed record CreateRequestDto(string Mode, string? MediaKey, string? Description, int? VoiceSeconds, int CategoryId,
    string? Size, string? Color, string Condition, long MaxPrice, int GovernorateId, bool ReadyToBuy, Guid? PageId = null);
public sealed record IdDto(Guid Id);
public sealed record RequestSummaryDto(Guid Id, long Number, string Title, string? Image, string Status, int OffersCount, int MaxOffers, DateTime CreatedAt, Guid? OrderId);
public sealed record AdBannerDto(Guid Id, Guid? PageId, string PageName, string Title, string Headline, int DiscountPercent, string? Image, string? DeepLink);
public sealed record LiveRequestDto(Guid Id, string Title, string? Image, long MaxPrice, int ReachedPages, int SeenBy,
    IReadOnlyList<long> OfferPrices, int MaxOffers, DateTime ExpandsAt, DateTime ExpiresAt, string Status,
    IReadOnlyList<string> WidenedTo, int WaitedMinutes, long? SuggestedPrice, AdBannerDto? Sponsored, string Governorate, Guid? OrderId);
public sealed record RaisePriceDto(long MaxPrice);

// ---- offers
public sealed record OfferDto(Guid Id, Guid PageId, string PageName, string? Image, long Price, double Rating, int Deals, bool Trusted,
    int DeliveryHours, string Condition, string Note, string PhotoStamp, bool Sponsored, bool Hidden, DateTime CreatedAt);
public sealed record OffersBoardDto(Guid RequestId, int MaxOffers, IReadOnlyList<OfferDto> Offers);
public sealed record SelectDto(Guid OfferId);
public sealed record SelectionDto(Guid RequestId, Guid OrderId, string OrderNo, string ItemTitle, OfferDto Offer, DateTime SelectedAt,
    string? PagePhone, int NoReplyAfterSec, string? WhatsAppLink);
public sealed record NoReplyResultDto(OfferDto? Next);
public sealed record SubmitOfferDto(long Price, int DeliveryHours, string Condition, string? Note, string PhotoKey);

// ---- orders
public sealed record OrderStepDto(string Title, string Detail, bool Done);
public sealed record RatingDto(int Stars, IReadOnlyList<string> Tags, string? Comment);
public sealed record OrderDto(Guid Id, string OrderNo, Guid PageId, string PageName, string? PageImage, IReadOnlyList<OrderStepDto> Steps,
    bool Received, string? Answer, RatingDto? Rating, string Status, IReadOnlyList<string> RatingTags, IReadOnlyList<string> LowRatingTags);
public sealed record DeliveryAnswerDto(string Answer);

// ---- trust
public sealed record ReportDto(string Target, Guid TargetId, Guid? OrderId, Guid? OfferId, string Reason, string? Text, string? PhotoKey = null);
public sealed record BlockDto(Guid? UserId, Guid? PageId);
public sealed record BlockedDto(Guid UserId, Guid? PageId, string Name);
public sealed record RegionDto(int GovernorateId, string Area);
public sealed record CreateDisputeDto(Guid? OrderId, Guid? SubjectId, string Type, string? Text, IReadOnlyList<string>? Photos);
public sealed record DisputeViewDto(Guid Id, Guid? OrderId, Guid? SubjectId, string Type, string Status, string Text, string? Decision, DateTime CreatedAt, DateTime DecideBy);

// ---- pages
public sealed record RegisterPageDto(string Name, string Instagram, IReadOnlyList<int> CategoryIds, IReadOnlyList<int> GovernorateIds, bool AcceptTerms, string? ImageKey = null);
public sealed record PageSettingsDto(bool Receiving, bool Sound, IReadOnlyList<int> CategoryIds, IReadOnlyList<int> GovernorateIds, int From, int To, IReadOnlyList<int> Days);
public sealed record PageMeDto(Guid Id, string Name, string Instagram, string Status, string? RejectReason, string BioCode, double Rating, int RatingCount,
    int Deals, bool Trusted, int FreeUsed, int FreeSelections, long Balance, DateTime? EarlyAccessUntil, DateTime? SuspendedUntil, string? Image, string? Bio,
    PageSettingsDto Settings, string? Phone);
public sealed record InboxItemDto(Guid Id, string Title, string? Image, string Governorate, string Condition, long MaxPrice, DateTime PostedAt,
    int Offers, DateTime? EarlyUntil, int EarlyTaken, bool OfferedByMe, bool Cancelled, string Category);
public sealed record SellerOfferDto(Guid Id, Guid RequestId, string Title, string? Image, long Price, string State, Guid? OrderId, string? OrderNo,
    DateTime? ChosenAt, bool Contacted, bool GhostReported, int? SelectionNo, string Governorate, string? CustomerPhone, Guid? CustomerId);
public sealed record LedgerDto(Guid Id, string Type, long Amount, string Note, DateTime At);
public sealed record TopUpDto(Guid Id, string Method, string Ref, long Amount, DateTime At, string Status);
public sealed record CreateTopUpDto(string Method, string TransactionRef, long Amount, string ReceiptKey);
public sealed record WalletDto(long Balance, long Available, int FreeUsed, int FreeSelections, long SelectionFee, bool BillingEnabled,
    IReadOnlyList<LedgerDto> Ledger, IReadOnlyList<TopUpDto> TopUps, IReadOnlyList<string> Methods, long MinTopUp);
public sealed record PageMarkDto(Guid Id, string Title, string Kind, Guid? OrderId, string? OrderNo, DateTime At, bool UnderReview);
public sealed record PageStandingDto(IReadOnlyList<PageMarkDto> Marks, int MarksForSuspension, DateTime? SuspendedUntil, string Status);
public sealed record ReceivedRatingDto(string OrderNo, int Stars, IReadOnlyList<string> Tags, string Comment, string Governorate, DateTime At);
public sealed record PublicPageDto(Guid Id, string Name, string? Image, string? Bio, double Rating, int RatingCount, int Deals, bool Trusted,
    IReadOnlyList<string> Categories, IReadOnlyList<string> Governorates, int ReplyMinutes, int MemberSince);
public sealed record PageSummaryDto(Guid Id, string Name, double Rating, int Deals, string? Image, bool IsAd);
public sealed record FeaturedPagesDto(string CategoryName, string Governorate, IReadOnlyList<PageSummaryDto> Pages);

// ---- ads
public sealed record CreateAdDto(string Slot, string Title, string? Headline, int DiscountPercent, IReadOnlyList<int> GovernorateIds,
    IReadOnlyList<int> CategoryIds, DateTime StartAt, int Days, string? ImageKey, string? DeepLink);
public sealed record AdDto(Guid Id, string Slot, string Title, string Headline, string Place, string? Image, string Status, string? Note,
    long Impressions, long Clicks, long Selections, DateTime StartAt, DateTime EndAt, long DailyPrice);
public sealed record AdEligibilityDto(bool Eligible, bool Verified, bool RatingOk, bool NoOpenReports, bool NotSuspended, double Rating, double MinRating);
public sealed record AdEventDto(string Type, string? DeviceId);

// ---- home / notifications / config
public sealed record StoryDto(Guid PageId, string Name, string? Image, bool IsAd);
public sealed record FeedItemDto(string Category, string Governorate, int Offers, string? Image);
public sealed record HomeStatsDto(int? VerifiedPages, int? Governorates, int? AvgFirstOfferMinutes);
public sealed record TrendingCategoryDto(int Id, string Name, string? Image, string Badge);
public sealed record DealDto(string Category, string PageName, long Saved, string? Image);
public sealed record HomeDto(string? FirstName, string? Area, string Governorate, bool HasUnreadNotifications, IReadOnlyList<StoryDto> Stories,
    LiveRequestDto? LiveRequest, IReadOnlyList<FeedItemDto> Ticker, HomeStatsDto Stats, IReadOnlyList<TrendingCategoryDto> Categories,
    IReadOnlyList<FeedItemDto> Feed, IReadOnlyList<DealDto> Deals, AdBannerDto? Ad, IReadOnlyList<PageSummaryDto> FeaturedPages);
public sealed record NotificationDto(Guid Id, string Title, string Body, string Route, DateTime At, bool Read);
public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor);

public sealed record ContactDto(string Topic, string Text);
public sealed record SupportMessageDto(Guid Id, Guid UserId, string Topic, string Text, string Status, string? Reply, DateTime At);

// ---- admin
public sealed record ReasonDto(string Reason);
public sealed record AdjustWalletDto(long Amount, string Reason);
public sealed record ResolveDto(bool Accept, string Decision);
public sealed record SuspendDto(int Days, string Reason);
public sealed record UpsertCategoryDto(string Slug, string Name, string? Emoji, string? ImageKey, string Badge, int SortOrder, bool IsActive, IReadOnlyList<string> Keywords, string FieldSchema);
public sealed record KeywordDto(string Word);
public sealed record KpiDto(int RequestsToday, int Requests7d, int Requests30d, double PctWithOffer, double? MedianFirstOfferMin, double SelectionRate,
    double ReceiptRate, double ComplaintRate, long Revenue, long AdsRevenue, int ActivePages, int PendingPages, int PendingTopUps, int OpenDisputes);
public sealed record AdminPageDto(Guid Id, string Name, string Instagram, string Status, string BioCode, double Rating, int Deals, long Balance, DateTime CreatedAt, string? Phone, DateTime? SuspendedUntil);
public sealed record AuditDto(Guid Id, Guid ActorUserId, string Action, string Entity, string EntityId, string Data, DateTime At);

public sealed record AdminTopUpDto(Guid Id, Guid PageId, string PageName, string Method, string Ref, long Amount, string? ReceiptUrl, DateTime At, string Status);
public sealed record AdminReportDto(Guid Id, Guid ReporterUserId, string Target, Guid TargetId, Guid? OrderId, Guid? OfferId, string Reason, string Text, string? PhotoUrl, string Status, DateTime At);
public sealed record AdminDisputeDto(Guid Id, Guid OpenedByUserId, Guid? OrderId, Guid? SubjectId, string Type, string Status, string Text, IReadOnlyList<string> Photos, string? Decision, DateTime CreatedAt, DateTime DecideBy);
public sealed record AdminUserDto(Guid Id, string Role, string? Name, string? Phone, string? Governorate, bool IsBanned, DateTime? PostingBannedUntil, int BanCount, DateTime CreatedAt);
public sealed record AdminCategoryDto(int Id, string Slug, string Name, string? Emoji, string? ImageKey, string? ImageUrl, string Badge, int SortOrder, bool IsActive, IReadOnlyList<string> Keywords, string FieldSchema);
public sealed record AdminAccountDto(Guid Id, string Email, string? Name, string AdminRole, bool TotpEnabled, DateTime CreatedAt);
public sealed record CreateAdminDto(string Email, string Name, string Password, string AdminRole);
public sealed record AdminRoleDto(string AdminRole);
public sealed record PricingDto(bool BillingEnabled, long SelectionFee, int FreeSelections, long EarlyAccessMonthly, int EarlyAccessDays, long BannerDaily,
    long FeaturedWeekly, long SponsoredOffer, long MinTopUp, IReadOnlyList<string> TopUpMethods);
public sealed record PagedDto<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
