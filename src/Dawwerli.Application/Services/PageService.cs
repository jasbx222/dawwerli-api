using System.Security.Cryptography;
using Dawwerli.Application.Abstractions;
using Dawwerli.Application.Contracts;
using Dawwerli.Domain.Common;
using Dawwerli.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dawwerli.Application.Services;

public sealed class PageService(Shared s, IPhoneProtector phones)
{
    private IAppDbContext Db => s.Db;
    private const string BioCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int BioCodeLength = 6;

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
            s.Url(p.ImageKey), p.Bio, ToSettings(p), user.PhoneEncrypted == "" ? null : phones.Decrypt(user.PhoneEncrypted));
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

    public async Task<Result<IReadOnlyList<InboxItemDto>>> InboxAsync(CancellationToken ct)
    {
        var res = await s.ActivePageAsync(ct);
        if (!res.IsSuccess) return res.Error!;
        var page = res.Value!;
        var now = s.Now;
        var rows = await Db.RequestDispatches.Where(d => d.PageId == page.Id)
            .Join(Db.Requests.Include(r => r.Governorate).Include(r => r.Category), d => d.RequestId, r => r.Id, (d, r) => r)
            .Where(r => r.Status == RequestStatus.Open || r.Status == RequestStatus.Expanded || r.Status == RequestStatus.Locked || r.Status == RequestStatus.Cancelled)
            .Where(r => r.ExpiresAt > now)
            .OrderByDescending(r => r.CreatedAt).Take(100)
            .Select(r => new { r, Mine = Db.Offers.Any(o => o.RequestId == r.Id && o.PageId == page.Id) })
            .ToListAsync(ct);
        await Db.RequestDispatches.Where(d => d.PageId == page.Id && d.SeenAt == null).ExecuteUpdateAsync(x => x.SetProperty(d => d.SeenAt, now), ct);
        IReadOnlyList<InboxItemDto> list = rows.Select(x => new InboxItemDto(x.r.Id, x.r.Title, s.Url(x.r.MediaKey), x.r.Governorate!.Name,
            x.r.Condition.Code(), x.r.MaxPrice, x.r.CreatedAt, x.r.OffersCount, x.r.EarlyUntil > x.r.CreatedAt ? x.r.EarlyUntil : null,
            x.r.EarlyOffersCount, x.Mine, x.r.Status == RequestStatus.Cancelled, x.r.Category!.Name)).ToList();
        return Result<IReadOnlyList<InboxItemDto>>.Ok(list);
    }

    public async Task<Result<IReadOnlyList<SellerOfferDto>>> MyOffersAsync(CancellationToken ct)
    {
        var page = await s.MyPageAsync(ct);
        if (page is null) return Errors.NotPage;
        var offers = await Db.Offers.Include(o => o.Request).ThenInclude(r => r!.Governorate).Where(o => o.PageId == page.Id)
            .OrderByDescending(o => o.CreatedAt).Take(100).ToListAsync(ct);
        var ids = offers.Select(o => o.Id).ToList();
        var orders = await Db.Orders.Where(o => ids.Contains(o.OfferId)).ToListAsync(ct);
        var selectionNos = await Db.LedgerEntries.Where(e => e.PageId == page.Id && (e.Type == LedgerType.FreeSelection || e.Type == LedgerType.SelectionFee))
            .OrderBy(e => e.CreatedAt).Select(e => e.OrderId).ToListAsync(ct);
        var customers = orders.Select(o => o.CustomerId).Distinct().ToList();
        var phoneMap = await Db.Users.Where(u => customers.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.PhoneEncrypted, ct);
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
            var phone = order is { PhoneVisible: true } && phoneMap.TryGetValue(order.CustomerId, out var enc) && enc != "" ? phones.Decrypt(enc) : null;
            var idx = order is null ? -1 : selectionNos.IndexOf(order.Id);
            return new SellerOfferDto(o.Id, o.RequestId, o.Request!.Title, s.Url(o.PhotoKey), o.Price, state, order?.Id,
                order is null ? null : Shared.OrderNo(order.Number), order?.SelectedAt, order?.ContactedAt is not null,
                order?.GhostReportedAt is not null, idx < 0 ? null : idx + 1, o.Request.Governorate!.Name, phone, order?.CustomerId);
        }).ToList();
        return Result<IReadOnlyList<SellerOfferDto>>.Ok(list);
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
        return new PublicPageDto(p.Id, p.Name, s.Url(p.ImageKey), p.Bio, Math.Round(p.Rating, 1), p.RatingCount, p.Deals, p.Deals >= rules.TrustedDeals,
            await Db.Categories.Where(c => catIds.Contains(c.Id)).Select(c => c.Name).ToListAsync(ct),
            await Db.Governorates.Where(g => govIds.Contains(g.Id)).Select(g => g.Name).ToListAsync(ct));
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
