using System.Security.Cryptography;
using Dawwerli.Application.Abstractions;
using Dawwerli.Application.Contracts;
using Dawwerli.Domain.Common;
using Dawwerli.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Dawwerli.Application.Services;

public sealed class PageService(Shared s, IPhoneProtector phones, IOptions<RegionOptions> region)
{
    private IAppDbContext Db => s.Db;
    private readonly TimeZoneInfo _tz = TimeZoneInfo.FindSystemTimeZoneById(region.Value.TimeZone);
    public const int FeedDefaultPageSize = 20, FeedMaxPageSize = 50, FeedMaxPageNumber = 10_000;
    public static readonly string[] FeedSince = ["all", "today", "week", "month"];
    public static readonly string[] FeedSorts = ["newest", "oldest", "priceAsc", "priceDesc"];
    public static readonly string[] OfferStates = ["incoming", "waiting", "chosen", "notChosen", "withdrawn", "closed"];
    private const string BioCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int BioCodeLength = 6;
    private const int ReplySample = 50;

    public static string NormalizeHandle(string h) => h.Trim().TrimStart('@').ToLowerInvariant();

    public async Task<Result<PageMeDto>> RegisterAsync(RegisterPageDto dto, CancellationToken ct)
    {
        if (!dto.AcceptTerms) return Error.Validation("terms.required", "Accept the terms");
        if (await s.MyPageAsync(ct) is not null) return Error.Conflict("page.exists", "You already have a page");
        var handle = NormalizeHandle(dto.Instagram);
        if (await Db.Pages.AnyAsync(p => p.InstagramHandle == handle, ct)) return Error.Conflict("page.handle_taken", "Instagram handle already registered");
        var cats = await Db.Categories.Where(c => dto.CategoryIds.Contains(c.Id)).Select(c => c.Id).ToListAsync(ct);
        var govs = await Db.Governorates.Where(g => dto.GovernorateIds.Contains(g.Id)).Select(g => g.Id).ToListAsync(ct);
        if (cats.Count == 0) return Error.Validation("categories.required", "Pick at least one category");
        if (govs.Count == 0) return Error.Validation("governorates.required", "Pick at least one governorate");
        var page = new Page
        {
            UserId = s.Me.UserId, Name = dto.Name.Trim(), InstagramHandle = handle, Status = PageStatus.Pending,
            BioCode = new string(Enumerable.Range(0, BioCodeLength).Select(_ => BioCodeAlphabet[RandomNumberGenerator.GetInt32(BioCodeAlphabet.Length)]).ToArray()),
            Categories = cats.Select(c => new PageCategory { CategoryId = c }).ToList(),
            Governorates = govs.Select(g => new PageGovernorate { GovernorateId = g }).ToList(),
            ImageKey = string.IsNullOrWhiteSpace(dto.ImageKey) ? null : dto.ImageKey.Trim(),
        };
        Db.Pages.Add(page);
        await Db.SaveChangesAsync(ct);
        return await MeAsync(ct);
    }

    public async Task<Result<PageMeDto>> MeAsync(CancellationToken ct)
    {
        var p = await s.MyPageAsync(ct);
        if (p is null) return Errors.NotPage;
        var rules = await s.Rules(ct);
        var user = await Db.Users.FirstAsync(u => u.Id == p.UserId, ct);
        return new PageMeDto(p.Id, p.Name, p.InstagramHandle, p.Status.Code(), p.RejectReason, p.BioCode, Math.Round(p.Rating, 1), p.RatingCount,
            p.Deals, p.Deals >= rules.TrustedDeals, p.FreeSelectionsUsed, rules.FreeSelections, p.Balance, p.EarlyAccessUntil, p.SuspendedUntil,
            s.Url(p.ImageKey), p.Bio, ToSettings(p), user.PhoneEncrypted == "" ? null : phones.Decrypt(user.PhoneEncrypted),
            await Db.PageFollows.CountAsync(f => f.PageId == p.Id, ct));
    }

    /// <summary>Re-submits a rejected page for review.</summary>
    public async Task<Result<PageMeDto>> ResubmitAsync(CancellationToken ct)
    {
        var p = await s.MyPageAsync(ct);
        if (p is null) return Errors.NotPage;
        if (p.Status != PageStatus.Rejected) return Error.Conflict("page.state", "Only rejected pages can resubmit");
        p.Status = PageStatus.Pending;
        p.RejectReason = null;
        await Db.SaveChangesAsync(ct);
        return await MeAsync(ct);
    }

    private static PageSettingsDto ToSettings(Page p) => new(p.Receiving, p.Sound, p.Categories.Select(c => c.CategoryId).ToList(),
        p.Governorates.Select(g => g.GovernorateId).ToList(), p.WorkFrom, p.WorkTo, p.WorkDays);

    public async Task<Result<PageSettingsDto>> SettingsAsync(CancellationToken ct)
    {
        var p = await s.MyPageAsync(ct);
        return p is null ? Errors.NotPage : ToSettings(p);
    }

    public async Task<Result<PageSettingsDto>> SaveSettingsAsync(PageSettingsDto dto, CancellationToken ct)
    {
        var p = await s.MyPageAsync(ct);
        if (p is null) return Errors.NotPage;
        var cats = await Db.Categories.Where(c => dto.CategoryIds.Contains(c.Id)).Select(c => c.Id).ToListAsync(ct);
        var govs = await Db.Governorates.Where(g => dto.GovernorateIds.Contains(g.Id)).Select(g => g.Id).ToListAsync(ct);
        if (cats.Count == 0 || govs.Count == 0) return Error.Validation("settings.empty", "Pick categories and governorates");
        p.Receiving = dto.Receiving;
        p.Sound = dto.Sound;
        p.WorkFrom = dto.From;
        p.WorkTo = dto.To;
        p.WorkDays = dto.Days.ToArray();
        Db.PageCategories.RemoveRange(p.Categories);
        Db.PageGovernorates.RemoveRange(p.Governorates);
        p.Categories = cats.Select(c => new PageCategory { PageId = p.Id, CategoryId = c }).ToList();
        p.Governorates = govs.Select(g => new PageGovernorate { PageId = p.Id, GovernorateId = g }).ToList();
        await Db.SaveChangesAsync(ct);
        return ToSettings(p);
    }

    /// <summary>
    /// Home feed for a page: live customer requests it can still offer on, filtered and paged on the server.
    /// since: all | today (Baghdad calendar day) | week (7 days) | month (30 days).
    /// sort: newest | oldest | priceAsc | priceDesc (on the customer's max price). Prices filter the max price, inclusive.
    /// </summary>
    public async Task<Result<PagedDto<InboxItemDto>>> FeedAsync(PageFeedQuery q, CancellationToken ct)
    {
        var res = await s.ActivePageAsync(ct);
        if (!res.IsSuccess) return res.Error!;
        var page = res.Value!;
        var since = Pick(q.Since, FeedSince, "all");
        if (since is null) return Errors.BadEnum("since");
        var sort = Pick(q.Sort, FeedSorts, "newest");
        if (sort is null) return Errors.BadEnum("sort");
        if (q.MinPrice < 0 || q.MaxPrice < 0) return Error.Validation("price.invalid", "Prices cannot be negative");
        if (q.MinPrice is { } lo && q.MaxPrice is { } hi && lo > hi) return Error.Validation("price.range", "Minimum price is above the maximum");
        if (q.CategoryId is <= 0) return Errors.BadEnum("categoryId");
        if (q.Page is < 1 || q.PageSize is < 1) return Error.Validation("paging.invalid", "Page and page size start at 1");
        var pageNo = Math.Min(q.Page ?? 1, FeedMaxPageNumber);
        var size = Math.Min(q.PageSize ?? FeedDefaultPageSize, FeedMaxPageSize);

        var now = s.Now;
        var rq = Db.Requests.AsNoTracking()
            .Where(r => (r.Status == RequestStatus.Open || r.Status == RequestStatus.Expanded) && r.ExpiresAt > now);
        if (q.CategoryId is { } cat) rq = rq.Where(r => r.CategoryId == cat);
        if (q.MinPrice is { } min) rq = rq.Where(r => r.MaxPrice >= min);
        if (q.MaxPrice is { } max) rq = rq.Where(r => r.MaxPrice <= max);
        DateTime? from = since switch
        {
            "today" => TodayStartUtc(now),
            "week" => now.AddDays(-7),
            "month" => now.AddDays(-30),
            _ => null,
        };
        if (from is { } f) rq = rq.Where(r => r.CreatedAt >= f);
        var total = await rq.CountAsync(ct);
        rq = sort switch
        {
            "oldest" => rq.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id),
            "priceAsc" => rq.OrderBy(r => r.MaxPrice).ThenByDescending(r => r.CreatedAt).ThenBy(r => r.Id),
            "priceDesc" => rq.OrderByDescending(r => r.MaxPrice).ThenByDescending(r => r.CreatedAt).ThenBy(r => r.Id),
            _ => rq.OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id),
        };
        var rows = await rq.Skip((pageNo - 1) * size).Take(size)
            .Select(r => new
            {
                r.Id, r.Description, r.MediaKey, Governorate = r.Governorate!.Name, r.Condition, r.MaxPrice, r.CreatedAt,
                r.OffersCount, r.EarlyUntil, r.EarlyOffersCount, r.Status, Category = r.Category!.Name, r.CategoryId,
                Mine = Db.Offers.Any(o => o.RequestId == r.Id && o.PageId == page.Id),
            })
            .ToListAsync(ct);
        if (pageNo == 1)
            await Db.RequestDispatches.Where(d => d.PageId == page.Id && d.SeenAt == null).ExecuteUpdateAsync(x => x.SetProperty(d => d.SeenAt, now), ct);
        var items = rows.Select(x => new InboxItemDto(x.Id, Shorten(x.Description), s.Url(x.MediaKey), x.Governorate, x.Condition.Code(), x.MaxPrice,
            x.CreatedAt, x.OffersCount, x.EarlyUntil > x.CreatedAt ? x.EarlyUntil : null, x.EarlyOffersCount, x.Mine, false, x.Category, x.CategoryId)).ToList();
        return new PagedDto<InboxItemDto>(items, total, pageNo, size);
    }

    private DateTime TodayStartUtc(DateTime nowUtc)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), _tz).Date;
        return DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), _tz), DateTimeKind.Utc);
    }

    /// <summary>Case-insensitive match against the allowed values; blank picks the default, unknown gives null.</summary>
    private static string? Pick(string? value, string[] allowed, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : allowed.FirstOrDefault(a => a.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase));

    private static string Shorten(string d) => d.Length > 60 ? d[..60] : d;

    public async Task<Result<IReadOnlyList<InboxItemDto>>> InboxAsync(CancellationToken ct)
    {
        var res = await s.ActivePageAsync(ct);
        if (!res.IsSuccess) return res.Error!;
        var page = res.Value!;
        var now = s.Now;
        var rows = await Db.Requests.AsNoTracking()
            .Where(r => r.Status == RequestStatus.Open || r.Status == RequestStatus.Expanded || r.Status == RequestStatus.Locked || r.Status == RequestStatus.Cancelled)
            .Where(r => r.ExpiresAt > now)
            .OrderByDescending(r => r.CreatedAt).Take(100)
            .Select(r => new
            {
                r.Id, r.Description, r.MediaKey, Governorate = r.Governorate!.Name, r.Condition, r.MaxPrice, r.CreatedAt,
                r.OffersCount, r.EarlyUntil, r.EarlyOffersCount, r.Status, Category = r.Category!.Name, r.CategoryId,
                Mine = Db.Offers.Any(o => o.RequestId == r.Id && o.PageId == page.Id),
            })
            .ToListAsync(ct);
        await Db.RequestDispatches.Where(d => d.PageId == page.Id && d.SeenAt == null).ExecuteUpdateAsync(x => x.SetProperty(d => d.SeenAt, now), ct);
        IReadOnlyList<InboxItemDto> list = rows.Select(x =>
        {
            var title = x.Description.Length > 60 ? x.Description[..60] : x.Description;
            return new InboxItemDto(x.Id, title, s.Url(x.MediaKey), x.Governorate, x.Condition.Code(), x.MaxPrice, x.CreatedAt,
                x.OffersCount, x.EarlyUntil > x.CreatedAt ? x.EarlyUntil : null, x.EarlyOffersCount, x.Mine,
                x.Status == RequestStatus.Cancelled, x.Category, x.CategoryId);
        }).ToList();
        return Result<IReadOnlyList<InboxItemDto>>.Ok(list);
    }

    /// <param name="state">Optional: incoming | waiting | chosen | notChosen | withdrawn | closed. Unknown values are rejected.</param>
    public async Task<Result<IReadOnlyList<SellerOfferDto>>> MyOffersAsync(CancellationToken ct, string? state = null)
    {
        var page = await s.MyPageAsync(ct);
        if (page is null) return Errors.NotPage;
        string? only = null;
        if (!string.IsNullOrWhiteSpace(state))
        {
            only = OfferStates.FirstOrDefault(x => x.Equals(state.Trim(), StringComparison.OrdinalIgnoreCase));
            if (only is null) return Errors.BadEnum("state");
        }
        var oq = Db.Offers.Include(o => o.Request).ThenInclude(r => r!.Governorate).Include(o => o.Request).ThenInclude(r => r!.Category)
            .Where(o => o.PageId == page.Id);
        var now = s.Now;
        oq = only switch
        {
            "chosen" => oq.Where(o => o.Status == OfferStatus.Selected),
            "notChosen" => oq.Where(o => o.Status == OfferStatus.NotSelected),
            "withdrawn" => oq.Where(o => o.Status == OfferStatus.Withdrawn),
            "waiting" => oq.Where(o => o.Status == OfferStatus.Active && (o.Request!.Status == RequestStatus.Open
                || o.Request.Status == RequestStatus.Expanded || o.Request.Status == RequestStatus.Locked)),
            "closed" => oq.Where(o => o.Status != OfferStatus.Selected && o.Status != OfferStatus.NotSelected && o.Status != OfferStatus.Withdrawn
                && !(o.Status == OfferStatus.Active && (o.Request!.Status == RequestStatus.Open
                    || o.Request.Status == RequestStatus.Expanded || o.Request.Status == RequestStatus.Locked))),
            "incoming" => oq.Where(o => false),
            _ => oq,
        };
        var offers = await oq.OrderByDescending(o => o.CreatedAt).Take(100).ToListAsync(ct);
        var ids = offers.Select(o => o.Id).ToList();
        var orders = await Db.Orders.Where(o => ids.Contains(o.OfferId)).ToListAsync(ct);
        var selectionNos = await Db.LedgerEntries.Where(e => e.PageId == page.Id && (e.Type == LedgerType.FreeSelection || e.Type == LedgerType.SelectionFee))
            .OrderBy(e => e.CreatedAt).Select(e => e.OrderId).ToListAsync(ct);
        // Customer phones are never listed: the page opens the linked store order to see the phone in that order's details.
        var orderIds = orders.Select(o => (Guid?)o.Id).ToList();
        var storeOrders = await Db.StoreOrders.Where(x => orderIds.Contains(x.OfferOrderId)).ToDictionaryAsync(x => x.OfferOrderId!.Value, x => x.Id, ct);
        IReadOnlyList<SellerOfferDto> list = offers.Select(o =>
        {
            var order = orders.Where(x => x.OfferId == o.Id).MaxBy(x => x.SelectedAt);
            var state = o.Status switch
            {
                OfferStatus.Selected => "chosen",
                OfferStatus.Active => o.Request!.IsLive ? "waiting" : "closed",
                OfferStatus.NotSelected => "notChosen",
                OfferStatus.Withdrawn => "withdrawn",
                _ => "closed",
            };
            var idx = order is null ? -1 : selectionNos.IndexOf(order.Id);
            return new SellerOfferDto(o.Id, o.RequestId, o.Request!.Title, s.Url(o.PhotoKey), o.Price, state, order?.Id,
                order is null ? null : Shared.OrderNo(order.Number), order?.SelectedAt, order?.ContactedAt is not null,
                order?.GhostReportedAt is not null, idx < 0 ? null : idx + 1, o.Request.Governorate!.Name, null, order?.CustomerId,
                o.CreatedAt, o.Request.Category?.Name, o.Request.MaxPrice,
                order is not null && storeOrders.TryGetValue(order.Id, out var soId) ? soId : null);
        }).ToList();
        if (only is not null && only != "incoming") return Result<IReadOnlyList<SellerOfferDto>>.Ok(list);
        var offered = offers.Select(o => o.RequestId).ToHashSet();
        // Requests made inside this page carry its id in the jsonb fields; match the key itself (a text LIKE does not work on jsonb).
        var pageKey = page.Id.ToString();
        var incoming = await Db.Requests.FromSqlInterpolated($"SELECT *, xmin FROM requests WHERE fields ->> 'pageId' = {pageKey}")
            .Include(r => r.Governorate)
            .Where(r => r.ExpiresAt > s.Now
                && (r.Status == RequestStatus.Open || r.Status == RequestStatus.Expanded || r.Status == RequestStatus.Locked))
            .OrderByDescending(r => r.CreatedAt).Take(50).ToListAsync(ct);
        var directed = incoming.Where(r => !offered.Contains(r.Id)).Select(r => new SellerOfferDto(r.Id, r.Id, r.Title, s.Url(r.MediaKey),
            r.MaxPrice, "incoming", null, null, null, false, false, null, r.Governorate?.Name ?? "", null, r.CustomerId, r.CreatedAt, null, r.MaxPrice));
        return Result<IReadOnlyList<SellerOfferDto>>.Ok(directed.Concat(list).Take(100).ToList());
    }

    public async Task<Result<PageStandingDto>> StandingAsync(CancellationToken ct)
    {
        var page = await s.MyPageAsync(ct);
        if (page is null) return Errors.NotPage;
        var rules = await s.Rules(ct);
        var since = s.Now.AddDays(-rules.MarksWindowDays);
        var marks = await Db.PageMarks.Where(m => m.PageId == page.Id && m.CreatedAt >= since).OrderByDescending(m => m.CreatedAt)
            .Select(m => new { m, No = Db.Orders.Where(o => o.Id == m.OrderId).Select(o => (long?)o.Number).FirstOrDefault() }).ToListAsync(ct);
        return new PageStandingDto(marks.Select(x => new PageMarkDto(x.m.Id, x.m.Title, x.m.Kind.Code(), x.m.OrderId,
                x.No is null ? null : Shared.OrderNo(x.No.Value), x.m.CreatedAt, x.m.UnderReview)).ToList(),
            rules.NoReplyMarksForSuspension, page.SuspendedUntil > s.Now ? page.SuspendedUntil : null, page.Status.Code());
    }

    public async Task<Result<IReadOnlyList<ReceivedRatingDto>>> RatingsAsync(CancellationToken ct)
    {
        var page = await s.MyPageAsync(ct);
        if (page is null) return Errors.NotPage;
        var rows = await Db.Ratings.Where(r => r.PageId == page.Id).OrderByDescending(r => r.CreatedAt).Take(100)
            .Join(Db.Orders.Include(o => o.Request).ThenInclude(r => r!.Governorate), r => r.OrderId, o => o.Id, (r, o) => new { r, o })
            .ToListAsync(ct);
        IReadOnlyList<ReceivedRatingDto> list = rows.Select(x => new ReceivedRatingDto(Shared.OrderNo(x.o.Number), x.r.Stars, x.r.Tags, x.r.Comment,
            x.o.Request!.Governorate!.Name, x.r.CreatedAt)).ToList();
        return Result<IReadOnlyList<ReceivedRatingDto>>.Ok(list);
    }

    public async Task<Result<PublicPageDto>> PublicAsync(Guid id, CancellationToken ct)
    {
        var p = await Db.Pages.Include(x => x.Categories).Include(x => x.Governorates).FirstOrDefaultAsync(x => x.Id == id && x.Status == PageStatus.Verified, ct);
        if (p is null) return Error.NotFound("page");
        var rules = await s.Rules(ct);
        var catIds = p.Categories.Select(c => c.CategoryId).ToList();
        var govIds = p.Governorates.Select(g => g.GovernorateId).ToList();
        var replies = await Db.Offers.Where(o => o.PageId == p.Id).OrderByDescending(o => o.CreatedAt).Take(ReplySample)
            .Join(Db.RequestDispatches.Where(d => d.PageId == p.Id), o => o.RequestId, d => d.RequestId, (o, d) => new { o.CreatedAt, d.DispatchedAt })
            .ToListAsync(ct);
        var replyMinutes = replies.Count == 0 ? 0 : (int)Math.Round(replies.Average(x => (x.CreatedAt - x.DispatchedAt).TotalMinutes));
        return new PublicPageDto(p.Id, p.Name, s.Url(p.ImageKey), p.Bio, Math.Round(p.Rating, 1), p.RatingCount, p.Deals, p.Deals >= rules.TrustedDeals,
            await Db.Categories.Where(c => catIds.Contains(c.Id)).Select(c => c.Name).ToListAsync(ct),
            await Db.Governorates.Where(g => govIds.Contains(g.Id)).OrderBy(g => g.SortOrder).Select(g => g.Name).ToListAsync(ct),
            replyMinutes, p.CreatedAt.Year);
    }

    /// <summary>Paid featured pages first («مُموّل»), then organic ranking by deals and rating.</summary>
    public async Task<Result<FeaturedPagesDto>> FeaturedAsync(int categoryId, int governorateId, int take, CancellationToken ct)
    {
        var cat = await Db.Categories.FirstOrDefaultAsync(c => c.Id == categoryId, ct);
        var gov = await Db.Governorates.FirstOrDefaultAsync(g => g.Id == governorateId, ct);
        if (cat is null || gov is null) return Error.NotFound("category");
        var now = s.Now;
        var paid = await Db.AdCampaigns.Where(a => a.Slot == AdSlot.FeaturedPage && a.Status == AdStatus.Active && a.StartAt <= now && a.EndAt > now
                && a.PageId != null && (a.CategoryIds.Length == 0 || a.CategoryIds.Contains(categoryId))
                && (a.GovernorateIds.Length == 0 || a.GovernorateIds.Contains(governorateId)))
            .Select(a => a.PageId!.Value).Distinct().ToListAsync(ct);
        var pages = await Db.Pages.Where(p => p.Status == PageStatus.Verified && (p.SuspendedUntil == null || p.SuspendedUntil < now)
                && p.Categories.Any(c => c.CategoryId == categoryId) && p.Governorates.Any(g => g.GovernorateId == governorateId))
            .OrderByDescending(p => p.Deals).ThenByDescending(p => p.Rating).Take(take).ToListAsync(ct);
        var list = pages.Select(p => new PageSummaryDto(p.Id, p.Name, Math.Round(p.Rating, 1), p.Deals, s.Url(p.ImageKey), paid.Contains(p.Id)))
            .OrderByDescending(p => p.IsAd).ToList();
        return new FeaturedPagesDto(cat.Name, gov.Name, list);
    }
}
