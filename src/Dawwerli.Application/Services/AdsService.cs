using Dawwerli.Application.Abstractions;
using Dawwerli.Application.Contracts;
using Dawwerli.Domain.Common;
using Dawwerli.Domain.Entities;
using Dawwerli.Domain.Rules;
using Microsoft.EntityFrameworkCore;

namespace Dawwerli.Application.Services;

public sealed class AdsService(Shared s, WalletService wallet, ITextCatalog texts)
{
    private IAppDbContext Db => s.Db;

    private IQueryable<AdCampaign> Running(AdSlot slot)
    {
        var now = s.Now;
        return Db.AdCampaigns.Where(a => a.Slot == slot && a.Status == AdStatus.Active && a.StartAt <= now && a.EndAt > now);
    }

    /// <summary>One banner for the governorate (and category when given), rotated randomly.</summary>
    public async Task<AdBannerDto?> BannerAsync(int governorateId, int? categoryId, CancellationToken ct)
    {
        var q = Running(AdSlot.HomeBanner).Include(a => a.Page)
            .Where(a => a.GovernorateIds.Length == 0 || a.GovernorateIds.Contains(governorateId));
        if (categoryId is { } c) q = q.Where(a => a.CategoryIds.Length == 0 || a.CategoryIds.Contains(c));
        var list = await q.ToListAsync(ct);
        if (list.Count == 0) return null;
        var a = list[Random.Shared.Next(list.Count)];
        return Banner(a);
    }

    private AdBannerDto Banner(AdCampaign a) => new(a.Id, a.PageId, a.Page?.Name ?? a.Title, a.Title, a.Headline, a.DiscountPercent,
        s.Url(a.ImageKey), a.DeepLink, s.Url(a.Page?.ImageKey), a.Page?.InstagramHandle, a.Page?.Status == PageStatus.Verified);

    public async Task<Result<AdEligibilityDto>> EligibilityAsync(CancellationToken ct)
    {
        var page = await s.MyPageAsync(ct);
        if (page is null) return Errors.NotPage;
        return await EligibilityAsync(page, ct);
    }

    private async Task<AdEligibilityDto> EligibilityAsync(Page page, CancellationToken ct)
    {
        var rules = await s.Rules(ct);
        var complaints = await Db.PageMarks.AnyAsync(m => m.PageId == page.Id && m.Kind == PageMarkKind.Complaint, ct);
        var ratingOk = page.Rating >= rules.MinAdRating || (page.RatingCount == 0 && !complaints);
        var noReports = !await Db.Reports.AnyAsync(r => r.Target == ReportTarget.Page && r.TargetId == page.Id &&
            (r.Status == CaseStatus.Open || r.Status == CaseStatus.UnderReview), ct);
        var verified = page.Status == PageStatus.Verified;
        var notSuspended = !(page.SuspendedUntil > s.Now);
        return new AdEligibilityDto(verified && ratingOk && noReports && notSuspended, verified, ratingOk, noReports, notSuspended,
            Math.Round(page.Rating, 1), rules.MinAdRating);
    }

    public static long DailyPrice(AdSlot slot, BusinessRules r) => slot switch
    {
        AdSlot.HomeBanner => r.BannerDaily,
        AdSlot.FeaturedPage => r.FeaturedWeekly / 7,
        _ => r.SponsoredOffer,
    };

    /// <summary>Live daily price per slot plus the sponsored popup timing, for the booking screens.</summary>
    public async Task<AdPricingDto> PricingAsync(CancellationToken ct)
    {
        var r = await s.Rules(ct);
        return new AdPricingDto(DailyPrice(AdSlot.HomeBanner, r), DailyPrice(AdSlot.FeaturedPage, r), DailyPrice(AdSlot.SponsoredOffer, r),
            r.PopupDurationSec, r.PopupIntervalMin, r.PopupFirstDelaySec, r.PopupMaxPerAdPerDay);
    }

    /// <summary>Popup timing set by the admin; read by the customer app before it shows a sponsored popup.</summary>
    public async Task<AdPopupSettingsDto> PopupSettingsAsync(CancellationToken ct)
    {
        var r = await s.Rules(ct);
        return new AdPopupSettingsDto(r.PopupDurationSec, r.PopupIntervalMin, r.PopupFirstDelaySec, r.PopupMaxPerAdPerDay);
    }

    public async Task<Result<AdDto>> CreateAsync(CreateAdDto dto, CancellationToken ct)
    {
        if (!EnumText.TryParse<AdSlot>(dto.Slot, out var slot)) return Errors.BadEnum("slot");
        if (dto.Days < 1) return Error.Validation("days.min", "Book at least one day");
        // The sponsored ad is shown as an image popup; without an image the app would always skip it.
        if (slot == AdSlot.SponsoredOffer && string.IsNullOrWhiteSpace(dto.ImageKey))
            return Error.Validation("ads.image", "A sponsored popup ad needs an image");
        var page = await s.MyPageAsync(ct);
        if (page is null) return Errors.NotPage;
        if (!(await EligibilityAsync(page, ct)).Eligible) return Error.Rule("ads.not_eligible", "Page is not eligible to advertise");
        var banned = await Db.BannedKeywords.Select(b => b.Word).ToListAsync(ct);
        var text = $"{dto.Title} {dto.Headline}";
        if (ContactFilter.HasContact(text) || ContactFilter.FindBanned(text, banned) is not null)
            return Error.Rule("ads.content", "Ad text has contact info or banned words");
        var rules = await s.Rules(ct);
        var price = DailyPrice(slot, rules);
        var total = price * dto.Days;
        var available = rules.BillingEnabled ? await wallet.AvailableAsync(page, rules, ct) : page.Balance;
        if (total > 0 && available < total) return Error.Payment("wallet.insufficient", "Top up your balance");
        var start = dto.StartAt < s.Now ? s.Now : dto.StartAt;
        var ad = new AdCampaign
        {
            OwnerType = AdOwnerType.Page, PageId = page.Id, Slot = slot, Title = dto.Title.Trim(), Headline = dto.Headline?.Trim() ?? "",
            DiscountPercent = dto.DiscountPercent, GovernorateIds = dto.GovernorateIds.ToArray(), CategoryIds = dto.CategoryIds.ToArray(),
            StartAt = start, EndAt = start.AddDays(dto.Days), DailyPrice = price, Status = AdStatus.Pending, ImageKey = dto.ImageKey,
            DeepLink = dto.DeepLink ?? $"/pages/p/{page.Id}",
        };
        if (total > 0)
        {
            wallet.Post(page, LedgerType.AdCharge, -total, texts.Get("ledger.adCharge", ad.Title), refId: ad.Id);
            ad.LastChargedOn = ad.EndAt.Date;
        }
        Db.AdCampaigns.Add(ad);
        await Db.SaveChangesAsync(ct);
        return await ToDtoAsync(ad, ct);
    }

    public async Task<Result<IReadOnlyList<AdDto>>> MineAsync(CancellationToken ct)
    {
        var page = await s.MyPageAsync(ct);
        if (page is null) return Errors.NotPage;
        var ads = await Db.AdCampaigns.Where(a => a.PageId == page.Id).OrderByDescending(a => a.CreatedAt).ToListAsync(ct);
        var list = new List<AdDto>();
        foreach (var a in ads) list.Add(await ToDtoAsync(a, ct));
        return list;
    }

    public async Task<Result> PauseAsync(Guid id, CancellationToken ct)
    {
        var page = await s.MyPageAsync(ct);
        if (page is null) return Errors.NotPage;
        var ad = await Db.AdCampaigns.FirstOrDefaultAsync(a => a.Id == id && a.PageId == page.Id, ct);
        if (ad is null) return Error.NotFound("ad");
        if (ad.Status is not (AdStatus.Active or AdStatus.Pending)) return Error.Conflict("ad.state", "Ad is not running");
        await RefundUnusedAsync(ad, page, ct);
        ad.Status = AdStatus.Ended;
        ad.EndAt = s.Now;
        await Db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<IReadOnlyList<AdBannerDto>> ServeAsync(string slot, int governorateId, int? categoryId, CancellationToken ct)
    {
        if (!EnumText.TryParse<AdSlot>(slot, out var sl)) return [];
        var q = Running(sl).Include(a => a.Page).Where(a => a.GovernorateIds.Length == 0 || a.GovernorateIds.Contains(governorateId));
        if (categoryId is { } c) q = q.Where(a => a.CategoryIds.Length == 0 || a.CategoryIds.Contains(c));
        return (await q.ToListAsync(ct)).Select(Banner).ToList();
    }

    /// <summary>Counts an impression/click, deduplicated per device per hour.</summary>
    public async Task<Result> TrackAsync(Guid id, AdEventDto dto, CancellationToken ct)
    {
        if (!EnumText.TryParse<AdEventType>(dto.Type, out var type)) return Errors.BadEnum("type");
        var device = dto.DeviceId ?? s.Me.DeviceKey ?? "anon";
        var hour = new DateTime(s.Now.Year, s.Now.Month, s.Now.Day, s.Now.Hour, 0, 0, DateTimeKind.Utc);
        if (await Db.AdEvents.AnyAsync(e => e.CampaignId == id && e.Type == type && e.DeviceKey == device && e.Hour == hour, ct)) return Result.Ok();
        var ad = await Db.AdCampaigns.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (ad is null) return Error.NotFound("ad");
        Db.AdEvents.Add(new AdEvent { CampaignId = id, Type = type, DeviceKey = device, Hour = hour });
        if (type == AdEventType.Click) ad.Clicks++; else ad.Impressions++;
        await Db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    /// <summary>Gives back unused prepaid days. Pending ads are refunded in full.</summary>
    public async Task RefundUnusedAsync(AdCampaign ad, Page page, CancellationToken ct)
    {
        if (ad.DailyPrice <= 0) return;
        var charged = await Db.LedgerEntries.Where(e => e.RefId == ad.Id && e.PageId == page.Id && e.Type == LedgerType.AdCharge).SumAsync(e => (long?)e.Amount, ct) ?? 0;
        var refunded = await Db.LedgerEntries.Where(e => e.RefId == ad.Id && e.PageId == page.Id && e.Type == LedgerType.AdRefund).SumAsync(e => (long?)e.Amount, ct) ?? 0;
        var held = -(charged + refunded);
        if (held <= 0) return;
        var unused = ad.Status == AdStatus.Pending ? BookedDays(ad) : (int)Math.Floor((ad.EndAt - s.Now).TotalDays);
        var refund = ad.Status == AdStatus.Pending ? held : Math.Min(held, Math.Max(0, unused) * ad.DailyPrice);
        if (refund <= 0) return;
        wallet.Post(page, LedgerType.AdRefund, refund, texts.Get("ledger.refund", ad.Title), refId: ad.Id);
    }

    /// <summary>Ends expired ads and charges any page ad that was booked without a payment.</summary>
    public async Task ChargeDailyAsync(CancellationToken ct)
    {
        await Db.AdCampaigns.Where(a => a.Status == AdStatus.Active && a.EndAt <= s.Now)
            .ExecuteUpdateAsync(x => x.SetProperty(a => a.Status, AdStatus.Ended), ct);
        var due = await Db.AdCampaigns.Include(a => a.Page).Where(a => a.PageId != null && a.DailyPrice > 0 && a.LastChargedOn == null
            && (a.Status == AdStatus.Pending || a.Status == AdStatus.Active)).ToListAsync(ct);
        foreach (var ad in due)
        {
            var page = ad.Page!;
            var total = ad.DailyPrice * BookedDays(ad);
            if (page.Balance < total) { ad.Status = AdStatus.PausedBalance; continue; }
            wallet.Post(page, LedgerType.AdCharge, -total, texts.Get("ledger.adCharge", ad.Title), refId: ad.Id);
            ad.LastChargedOn = ad.EndAt.Date;
        }
        await Db.SaveChangesAsync(ct);
    }

    private static int BookedDays(AdCampaign ad)
    {
        var days = (int)Math.Round((ad.EndAt - ad.StartAt).TotalDays);
        return days < 1 ? 1 : days;
    }

    public async Task<AdDto> ToDtoAsync(AdCampaign a, CancellationToken ct)
    {
        var govs = await Db.Governorates.Where(g => a.GovernorateIds.Contains(g.Id)).Select(g => g.Name).ToListAsync(ct);
        var cats = await Db.Categories.Where(c => a.CategoryIds.Contains(c.Id)).Select(c => c.Name).ToListAsync(ct);
        var place = string.Join(" · ", cats.Concat(govs));
        return new AdDto(a.Id, a.Slot.Code(), a.Title, a.Headline, place, s.Url(a.ImageKey), a.Status.Code(), a.Note, a.Impressions, a.Clicks,
            a.Selections, a.StartAt, a.EndAt, a.DailyPrice);
    }
}
