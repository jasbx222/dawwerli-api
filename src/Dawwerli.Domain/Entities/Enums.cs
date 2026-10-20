namespace Dawwerli.Domain.Entities;

public enum UserRole { Customer, Page, Admin }
public enum OtpChannel { Sms, WhatsApp }
public enum PageStatus { Pending, Verified, Rejected, Suspended, Banned }
public enum InputMode { Photo, Text, Voice }
public enum ItemCondition { New, Used, Any }
public enum RequestStatus { Open, Expanded, Locked, Selected, Closed, Expired, Cancelled }
public enum OfferStatus { Active, Selected, NotSelected, Withdrawn, Closed }
public enum OrderStatus { InPurchase, AwaitingDelivery, Delivered, NotDelivered, Mismatch, Rated, NoReply, Disputed, Resolved, Closed }
public enum DeliveryAnswer { Received, NotReceived, Mismatch }
public enum LedgerType { TopUp, SelectionFee, FreeSelection, AdCharge, AdRefund, EarlyAccess, Adjustment }
public enum TopUpStatus { Pending, Approved, Rejected }
public enum AdSlot { HomeBanner, FeaturedPage, SponsoredOffer }
public enum AdOwnerType { Page, Brand, Platform }
public enum AdStatus { Pending, Rejected, PausedBalance, PausedSuspended, Active, Ended }
public enum AdEventType { Impression, Click }
public enum ReportTarget { Page, Customer }
public enum ReportReason { PriceChanged, NoReply, Abuse, ContactSolicitation, OffAppPayment, DepositRequest, BannedItem, Fake, Other, NotDelivered, Mismatch }
public enum CaseStatus { Open, UnderReview, Accepted, Rejected }
public enum DisputeType { NoReply, NotDelivered, FakeReport, StrikeAppeal }
public enum PageMarkKind { NoReply, Complaint }
public enum CategoryBadge { None, Trending, New }
