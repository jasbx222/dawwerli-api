using System.Text.Json;
using Dawwerli.Application.Abstractions;
using Dawwerli.Application.Contracts;
using Dawwerli.Domain.Common;
using Dawwerli.Domain.Entities;
using Dawwerli.Domain.Rules;
using Microsoft.EntityFrameworkCore;

namespace Dawwerli.Application.Services;

/// <summary>Back-office operations. Every write is audited.</summary>
public sealed class AdminService(Shared s, WalletService wallet, TrustService trust, AdsService ads, INotifier notifier, ITextCatalog texts,
    IRulesProvider rulesProvider, IPhoneProtector phones,
    IPasswordHasher passwords, Microsoft.Extensions.Options.IOptions<AdminAuthOptions> authOptions, Microsoft.Extensions.Options.IOptions<AuditPagingOptions> auditOptions)
{
    private IAppDbContext Db => s.Db;
    public const string CategoryImageFolder = "categories";

    public async Task<IReadOnlyList<AdminPageDto>> PagesAsync(string? status, CancellationToken ct)
    {
        var q = Db.Pages.Include(p => p.User).AsQueryable();
        var now = s.Now;
        if (EnumText.TryParse<PageStatus>(status, out var st))
            q = st == PageStatus.Suspended ? q.Where(p => p.Status == st || p.SuspendedUntil > now)
              : st == PageStatus.Verified ? q.Where(p => p.Status == st && (p.SuspendedUntil == null || p.SuspendedUntil <= now)) : q.Where(p => p.Status == st);
        q = st == PageStatus.Pending ? q.OrderBy(p => p.CreatedAt) : q.OrderByDescending(p => p.CreatedAt);
        return (await q.Take(200).ToListAsync(ct)).Select(p => new AdminPageDto(p.Id, p.Name, p.InstagramHandle,
            p.Status.Code(), p.BioCode, p.Rating, p.Deals, p.Balance, p.CreatedAt,
            p.User!.PhoneEncrypted == "" ? null : phones.Decrypt(p.User.PhoneEncrypted), p.SuspendedUntil > now ? p.SuspendedUntil : null)).ToList();
    }

    private async Task<Result<Page>> PageAsync(Guid id, CancellationToken ct) =>
        await Db.Pages.FirstOrDefaultAsync(p => p.Id == id, ct) is { } p ? p : Error.NotFound("page");

    public async Task<Result> VerifyPageAsync(Guid id, CancellationToken ct)
    {
        var res = await PageAsync(id, ct);
        if (!res.IsSuccess) return res.Error!;
        var p = res.Value!;
        p.Status = PageStatus.Verified;
        p.RejectReason = null;
        s.Audit("page.verify", nameof(Page), id);
        await Db.SaveChangesAsync(ct);
        await notifier.NotifyAsync(p.UserId, texts.Get("page.verified.title"), texts.Get("page.verified.body"), "/seller", ct);
        return Result.Ok();
    }

    public async Task<Result> RejectPageAsync(Guid id, ReasonDto dto, CancellationToken ct)
    {
        var res = await PageAsync(id, ct);
        if (!res.IsSuccess) return res.Error!;
        var p = res.Value!;
        p.Status = PageStatus.Rejected;
        p.RejectReason = dto.Reason;
        s.Audit("page.reject", nameof(Page), id, JsonSerializer.Serialize(dto));
        await Db.SaveChangesAsync(ct);
        await notifier.NotifyAsync(p.UserId, texts.Get("page.rejected.title"), dto.Reason, "/seller/verify/rejected", ct);
        return Result.Ok();
    }

    public async Task<Result> SuspendPageAsync(Guid id, SuspendDto dto, CancellationToken ct)
    {
        var res = await PageAsync(id, ct);
        if (!res.IsSuccess) return res.Error!;
        var p = res.Value!;
        p.SuspendedUntil = s.Now.AddDays(dto.Days);
        await Db.AdCampaigns.Where(a => a.PageId == id && a.Status == AdStatus.Active).ExecuteUpdateAsync(x => x.SetProperty(a => a.Status, AdStatus.PausedSuspended), ct);
        s.Audit("page.suspend", nameof(Page), id, JsonSerializer.Serialize(dto));
        await Db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    /// <summary>Ends a suspension early and resumes ads that the suspension paused.</summary>
    public async Task<Result> UnsuspendPageAsync(Guid id, ReasonDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Reason)) return Error.Validation("reason.required", "Reason is required");
        var res = await PageAsync(id, ct);
        if (!res.IsSuccess) return res.Error!;
        var p = res.Value!;
        if (!(p.SuspendedUntil > s.Now)) return Error.Conflict("page.not_suspended", "Page is not suspended");
        p.SuspendedUntil = null;
        await Db.AdCampaigns.Where(a => a.PageId == id && a.Status == AdStatus.PausedSuspended).ExecuteUpdateAsync(x => x.SetProperty(a => a.Status, AdStatus.Active), ct);
        s.Audit("page.unsuspend", nameof(Page), id, JsonSerializer.Serialize(dto));
        await Db.SaveChangesAsync(ct);
        await notifier.NotifyAsync(p.UserId, texts.Get("page.unsuspended"), dto.Reason, "/seller/home", ct);
        return Result.Ok();
    }

    public async Task<Result> BanPageAsync(Guid id, ReasonDto dto, CancellationToken ct)
    {
        var res = await PageAsync(id, ct);
        if (!res.IsSuccess) return res.Error!;
        res.Value!.Status = PageStatus.Banned;
        await Db.Users.Where(u => u.Id == res.Value.UserId).ExecuteUpdateAsync(x => x.SetProperty(u => u.IsBanned, true), ct);
        await Db.AdCampaigns.Where(a => a.PageId == id && a.Status == AdStatus.Active).ExecuteUpdateAsync(x => x.SetProperty(a => a.Status, AdStatus.PausedSuspended), ct);
        s.Audit("page.ban", nameof(Page), id, JsonSerializer.Serialize(dto));
        await Db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<Result> SetUserBanAsync(Guid userId, bool banned, CancellationToken ct)
    {
        var u = await Db.Users.FirstOrDefaultAsync(x => x.Id == userId, ct);
        if (u is null) return Error.NotFound("user");
        u.IsBanned = banned;
        if (!banned) u.PostingBannedUntil = null;
        s.Audit(banned ? "user.ban" : "user.unban", nameof(User), userId);
        await Db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<IReadOnlyList<AdminTopUpDto>> TopUpsAsync(string? status, CancellationToken ct)
    {
        var st = EnumText.TryParse<TopUpStatus>(status, out var x) ? x : TopUpStatus.Pending;
        var rows = await (from t in Db.TopUps join p in Db.Pages on t.PageId equals p.Id where t.Status == st orderby t.CreatedAt
            select new { t, p.Name }).Take(200).ToListAsync(ct);
        return rows.Select(r => new AdminTopUpDto(r.t.Id, r.t.PageId, r.Name, r.t.Method, r.t.TransactionRef, r.t.Amount, s.Url(r.t.ReceiptKey),
            r.t.CreatedAt, r.t.Status.Code())).ToList();
    }

    public async Task<IReadOnlyList<AdminUserDto>> UsersAsync(string? role, bool? banned, CancellationToken ct)
    {
        var q = Db.Users.Include(u => u.Governorate).Where(u => u.DeletedAt == null);
        if (EnumText.TryParse<UserRole>(role, out var r)) q = q.Where(u => u.Role == r);
        if (banned is { } b) q = q.Where(u => u.IsBanned == b);
        return (await q.OrderByDescending(u => u.CreatedAt).Take(200).ToListAsync(ct)).Select(u => new AdminUserDto(u.Id, u.Role.Code(), u.Name,
            u.PhoneEncrypted == "" ? null : phones.Decrypt(u.PhoneEncrypted), u.Governorate?.Name, u.IsBanned, u.PostingBannedUntil, u.BanCount, u.CreatedAt)).ToList();
    }

    public async Task<Result> ReviewTopUpAsync(Guid id, bool approve, CancellationToken ct)
    {
        await using var tx = await Db.BeginTransactionAsync(ct);
        var t = await Db.TopUps.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t is null) return Error.NotFound("topup");
        if (t.Status != TopUpStatus.Pending) return Error.Conflict("topup.state", "Already reviewed");
        var page = await Db.Pages.FirstAsync(p => p.Id == t.PageId, ct);
        t.Status = approve ? TopUpStatus.Approved : TopUpStatus.Rejected;
        t.ReviewedAt = s.Now;
        if (approve)
        {
            wallet.Post(page, LedgerType.TopUp, t.Amount, texts.Get("ledger.topup", t.Method, t.TransactionRef), refId: t.Id);
            await Db.AdCampaigns.Where(a => a.PageId == page.Id && a.Status == AdStatus.PausedBalance)
                .ExecuteUpdateAsync(x => x.SetProperty(a => a.Status, AdStatus.Active), ct);
        }
        s.Audit(approve ? "topup.approve" : "topup.reject", nameof(TopUp), id);
        await Db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await notifier.NotifyAsync(page.UserId, texts.Get(approve ? "topup.approved" : "topup.rejected"), $"{t.Amount}", "/seller/wallet", ct);
        return Result.Ok();
    }

    public async Task<Result> AdjustWalletAsync(Guid pageId, AdjustWalletDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Reason)) return Error.Validation("reason.required", "Reason is required");
        var res = await PageAsync(pageId, ct);
        if (!res.IsSuccess) return res.Error!;
        wallet.Post(res.Value!, LedgerType.Adjustment, dto.Amount, dto.Reason);
        s.Audit("wallet.adjust", nameof(Page), pageId, JsonSerializer.Serialize(dto));
        await Db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<IReadOnlyList<AdDto>> AdsAsync(string? status, CancellationToken ct)
    {
        var q = Db.AdCampaigns.AsQueryable();
        if (EnumText.TryParse<AdStatus>(status, out var st)) q = q.Where(a => a.Status == st);
        var list = new List<AdDto>();
        foreach (var a in await q.OrderByDescending(a => a.CreatedAt).Take(200).ToListAsync(ct)) list.Add(await ads.ToDtoAsync(a, ct));
        return list;
    }

    public async Task<Result> ReviewAdAsync(Guid id, bool approve, string? note, CancellationToken ct)
    {
        var ad = await Db.AdCampaigns.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (ad is null) return Error.NotFound("ad");
        if (ad.Status != AdStatus.Pending) return Error.Conflict("ad.state", "Ad is not pending");
        ad.Status = approve ? AdStatus.Active : AdStatus.Rejected;
        ad.Note = note;
        s.Audit(approve ? "ad.approve" : "ad.reject", nameof(AdCampaign), id);
        await Db.SaveChangesAsync(ct);
        if (ad.PageId is { } pid && await Db.Pages.Where(p => p.Id == pid).Select(p => p.UserId).FirstOrDefaultAsync(ct) is { } uid)
            await notifier.NotifyAsync(uid, texts.Get(approve ? "ad.approved" : "ad.rejected"), note ?? ad.Title, "/seller/ads/mine", ct);
        return Result.Ok();
    }

    public async Task<IReadOnlyList<AdminDisputeDto>> DisputesAsync(CancellationToken ct) =>
        (await Db.Disputes.Where(d => d.Status == CaseStatus.Open || d.Status == CaseStatus.UnderReview).OrderBy(d => d.DecideBy).ToListAsync(ct))
        .Select(d => new AdminDisputeDto(d.Id, d.OpenedByUserId, d.OrderId, d.SubjectId, d.Type.Code(), d.Status.Code(), d.Text,
            d.Photos.Select(k => s.Url(k)).OfType<string>().ToList(), d.Decision, d.CreatedAt, d.DecideBy)).ToList();

    /// <summary>Accept = the opener is right. Applies strikes/marks per the rules.</summary>
    public async Task<Result> ResolveDisputeAsync(Guid id, ResolveDto dto, CancellationToken ct)
    {
        var d = await Db.Disputes.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (d is null) return Error.NotFound("dispute");
        if (d.Status is CaseStatus.Accepted or CaseStatus.Rejected) return Error.Conflict("dispute.state", "Already resolved");
        var rules = await s.Rules(ct);
        var order = d.OrderId is null ? null : await Db.Orders.Include(o => o.Page).FirstOrDefaultAsync(o => o.Id == d.OrderId, ct);
        switch (d.Type)
        {
            case DisputeType.StrikeAppeal:
                if (dto.Accept)
                {
                    await Db.Strikes.Where(x => x.Id == d.SubjectId).ExecuteDeleteAsync(ct);
                    await Db.PageMarks.Where(x => x.Id == d.SubjectId).ExecuteDeleteAsync(ct);
                }
                else
                {
                    await Db.Strikes.Where(x => x.Id == d.SubjectId).ExecuteUpdateAsync(x => x.SetProperty(k => k.Frozen, false), ct);
                    await Db.PageMarks.Where(x => x.Id == d.SubjectId).ExecuteUpdateAsync(x => x.SetProperty(k => k.UnderReview, false), ct);
                }
                break;
            case DisputeType.NoReply when order is not null:
                // Raised by the system when the page said it contacted the customer but the customer tapped «ما ردت».
                if (dto.Accept) await trust.AddStrikeAsync(order.CustomerId, texts.Get("strike.falseNoReply"), order.Id, rules, ct);
                else await trust.AddPageMarkAsync(order.Page!, PageMarkKind.NoReply, order.Id, texts.Get("mark.noReply", Shared.OrderNo(order.Number)), rules, ct);
                order.Status = OrderStatus.Resolved;
                break;
            case DisputeType.NotDelivered when order is not null:
                if (dto.Accept) await trust.AddPageMarkAsync(order.Page!, PageMarkKind.Complaint, order.Id, texts.Get("mark.complaint", Shared.OrderNo(order.Number)), rules, ct);
                else await trust.AddStrikeAsync(d.OpenedByUserId, texts.Get("strike.falseComplaint"), order.Id, rules, ct);
                order.Status = OrderStatus.Resolved;
                break;
            case DisputeType.FakeReport when order is not null:
                if (dto.Accept)
                    await Db.Strikes.Where(x => x.OrderId == order.Id && x.UserId == d.OpenedByUserId).ExecuteDeleteAsync(ct);
                break;
        }
        d.Status = dto.Accept ? CaseStatus.Accepted : CaseStatus.Rejected;
        d.Decision = dto.Decision;
        d.ResolvedAt = s.Now;
        s.Audit("dispute.resolve", nameof(Dispute), id, JsonSerializer.Serialize(dto));
        await Db.SaveChangesAsync(ct);
        await notifier.NotifyAsync(d.OpenedByUserId, texts.Get("dispute.resolved"), dto.Decision, $"/account/dispute/status?id={d.Id}", ct);
        return Result.Ok();
    }

    public async Task<IReadOnlyList<AdminReportDto>> ReportsAsync(CancellationToken ct) =>
        (await Db.Reports.Where(r => r.Status == CaseStatus.Open || r.Status == CaseStatus.UnderReview).OrderBy(r => r.CreatedAt).ToListAsync(ct))
        .Select(r => new AdminReportDto(r.Id, r.ReporterUserId, r.Target.Code(), r.TargetId, r.OrderId, r.OfferId, r.Reason.Code(), r.Text,
            s.Url(r.PhotoKey), r.Status.Code(), r.CreatedAt)).ToList();

    /// <summary>Accept = report is valid: strike the customer / complaint-mark the page. A ghost report also refunds the page.</summary>
    public async Task<Result> ResolveReportAsync(Guid id, ResolveDto dto, CancellationToken ct)
    {
        var r = await Db.Reports.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return Error.NotFound("report");
        if (r.Status is CaseStatus.Accepted or CaseStatus.Rejected) return Error.Conflict("report.state", "Already resolved");
        var rules = await s.Rules(ct);
        if (dto.Accept)
        {
            if (r.Target == ReportTarget.Customer)
            {
                await trust.AddStrikeAsync(r.TargetId, dto.Decision, r.OrderId, rules, ct);
                if (r.Reason == ReportReason.NoReply && r.OrderId is { } oid && await Db.Orders.Include(o => o.Page).FirstOrDefaultAsync(o => o.Id == oid, ct) is { } o)
                {
                    wallet.ReleaseSelection(o.Page!, o);
                    o.Status = OrderStatus.Closed;
                }
            }
            else if (r.Target == ReportTarget.Product)
            {
                // Valid product report: hide it from customers (admin can unhide later).
                if (await Db.Products.FirstOrDefaultAsync(p => p.Id == r.TargetId, ct) is { } product) product.HiddenByAdmin = true;
            }
            else if (await Db.Pages.FirstOrDefaultAsync(p => p.Id == r.TargetId, ct) is { } page)
                await trust.AddPageMarkAsync(page, PageMarkKind.Complaint, r.OrderId, dto.Decision, rules, ct);
        }
        r.Status = dto.Accept ? CaseStatus.Accepted : CaseStatus.Rejected;
        s.Audit("report.resolve", nameof(Report), id, JsonSerializer.Serialize(dto));
        await Db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<IReadOnlyList<SupportMessageDto>> MessagesAsync(CancellationToken ct) =>
        await Db.SupportMessages.Where(m => m.Status == CaseStatus.Open).OrderBy(m => m.CreatedAt)
            .Select(m => new SupportMessageDto(m.Id, m.UserId, m.Topic, m.Text, m.Status.ToString(), m.Reply, m.CreatedAt)).ToListAsync(ct);

    public async Task<Result> ReplyAsync(Guid id, ReasonDto dto, CancellationToken ct)
    {
        var m = await Db.SupportMessages.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (m is null) return Error.NotFound("message");
        m.Reply = dto.Reason;
        m.Status = CaseStatus.Accepted;
        s.Audit("contact.reply", nameof(SupportMessage), id);
        await Db.SaveChangesAsync(ct);
        await notifier.NotifyAsync(m.UserId, texts.Get("contact.replied"), dto.Reason, "/notifications", ct);
        return Result.Ok();
    }

    public Task<BusinessRules> SettingsAsync(CancellationToken ct) => rulesProvider.GetAsync(ct);

    public async Task<Result> SaveSettingsAsync(BusinessRules rules, CancellationToken ct)
    {
        if (rules.MaxOffers < 1 || rules.MaxEarlySlots < 0 || rules.MaxEarlySlots > rules.MaxOffers)
            return Error.Validation("settings.slots", "MaxEarlySlots must be between 0 and MaxOffers");
        await rulesProvider.UpdateAsync(rules, ct);
        s.Audit("settings.update", "Settings", 1, JsonSerializer.Serialize(rules));
        await Db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<IReadOnlyList<AdminCategoryDto>> CategoriesAsync(CancellationToken ct) =>
        (await Db.Categories.OrderBy(c => c.SortOrder).ThenBy(c => c.Id).ToListAsync(ct)).Select(c => new AdminCategoryDto(c.Id, c.Slug, c.Name, c.Emoji,
            c.ImageKey, s.Url(c.ImageKey), c.Badge.Code(), c.SortOrder, c.IsActive, c.Keywords, c.FieldSchema)).ToList();

    public async Task<Result<int>> UpsertCategoryAsync(int? id, UpsertCategoryDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Name) || string.IsNullOrWhiteSpace(dto.Slug)) return Error.Validation("category.required", "Name and slug are required");
        if (dto.ImageKey is { } key && !key.StartsWith(CategoryImageFolder + "/", StringComparison.Ordinal)) return Error.Validation("category.image", "Image must be uploaded as a category image");
        if (await Db.Categories.AnyAsync(x => x.Slug == dto.Slug && x.Id != (id ?? 0), ct)) return Error.Conflict("category.slug", "Slug already used");
        if (!EnumText.TryParse<CategoryBadge>(dto.Badge, out var badge)) return Errors.BadEnum("badge");
        try { JsonDocument.Parse(dto.FieldSchema); } catch (JsonException) { return Error.Validation("schema.json", "Field schema must be JSON"); }
        var c = id is null ? null : await Db.Categories.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (id is not null && c is null) return Error.NotFound("category");
        if (c is null) Db.Categories.Add(c = new Category());
        c.Slug = dto.Slug; c.Name = dto.Name; c.Emoji = dto.Emoji; c.ImageKey = dto.ImageKey; c.Badge = badge;
        c.SortOrder = dto.SortOrder; c.IsActive = dto.IsActive; c.Keywords = dto.Keywords.ToArray(); c.FieldSchema = dto.FieldSchema;
        await Db.SaveChangesAsync(ct);
        s.Audit(id is null ? "category.create" : "category.update", nameof(Category), c.Id);
        await Db.SaveChangesAsync(ct);
        return c.Id;
    }

    public async Task<IReadOnlyList<BannedKeyword>> KeywordsAsync(CancellationToken ct) => await Db.BannedKeywords.OrderBy(k => k.Word).ToListAsync(ct);

    public async Task<Result> AddKeywordAsync(KeywordDto dto, CancellationToken ct)
    {
        var w = dto.Word.Trim();
        if (w.Length == 0) return Error.Validation("word.required", "Word is required");
        if (!await Db.BannedKeywords.AnyAsync(k => k.Word == w, ct)) Db.BannedKeywords.Add(new BannedKeyword { Word = w });
        s.Audit("keyword.add", nameof(BannedKeyword), w);
        await Db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<Result> RemoveKeywordAsync(int id, CancellationToken ct)
    {
        await Db.BannedKeywords.Where(k => k.Id == id).ExecuteDeleteAsync(ct);
        s.Audit("keyword.remove", nameof(BannedKeyword), id);
        await Db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<KpiDto> KpisAsync(CancellationToken ct)
    {
        var now = s.Now;
        var since30 = now.AddDays(-30);
        var req30 = Db.Requests.Where(r => r.CreatedAt >= since30);
        var total30 = await req30.CountAsync(ct);
        var withOffer = await req30.CountAsync(r => r.OffersCount > 0, ct);
        var orders30 = Db.Orders.Where(o => o.SelectedAt >= since30);
        var selected = await orders30.Select(o => o.RequestId).Distinct().CountAsync(ct);
        var received = await orders30.CountAsync(o => o.Answer == DeliveryAnswer.Received, ct);
        var complaints = await orders30.CountAsync(o => o.Answer == DeliveryAnswer.NotReceived || o.Answer == DeliveryAnswer.Mismatch, ct);
        var ordersCount = await orders30.CountAsync(ct);
        var firsts = await req30.Where(r => r.OffersCount > 0)
            .Select(r => new { r.CreatedAt, First = Db.Offers.Where(o => o.RequestId == r.Id).Min(o => o.CreatedAt) }).ToListAsync(ct);
        var mins = firsts.Select(x => (x.First - x.CreatedAt).TotalMinutes).Order().ToList();
        var revenue = -await Db.LedgerEntries.Where(e => e.Type == LedgerType.SelectionFee && e.CreatedAt >= since30).SumAsync(e => e.Amount, ct);
        var adsRevenue = -await Db.LedgerEntries.Where(e => (e.Type == LedgerType.AdCharge || e.Type == LedgerType.EarlyAccess) && e.CreatedAt >= since30).SumAsync(e => e.Amount, ct);
        double Pct(int a, int b) => b == 0 ? 0 : Math.Round(100.0 * a / b, 1);
        return new KpiDto(await Db.Requests.CountAsync(r => r.CreatedAt >= now.Date, ct), await Db.Requests.CountAsync(r => r.CreatedAt >= now.AddDays(-7), ct),
            total30, Pct(withOffer, total30), mins.Count == 0 ? null : mins[mins.Count / 2], Pct(selected, withOffer), Pct(received, ordersCount),
            Pct(complaints, ordersCount), revenue, adsRevenue,
            await Db.Pages.CountAsync(p => p.Status == PageStatus.Verified, ct), await Db.Pages.CountAsync(p => p.Status == PageStatus.Pending, ct),
            await Db.TopUps.CountAsync(t => t.Status == TopUpStatus.Pending, ct),
            await Db.Disputes.CountAsync(d => d.Status == CaseStatus.Open || d.Status == CaseStatus.UnderReview, ct));
    }

    public async Task<PagedDto<AuditDto>> AuditAsync(int? page, int? pageSize, string? action, CancellationToken ct)
    {
        var o = auditOptions.Value;
        var size = Math.Clamp(pageSize ?? o.DefaultPageSize, 1, o.MaxPageSize);
        var number = Math.Max(page ?? 1, 1);
        var q = Db.AuditLogs.AsQueryable();
        if (!string.IsNullOrWhiteSpace(action)) q = q.Where(a => a.Action.StartsWith(action));
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(a => a.CreatedAt).Skip((number - 1) * size).Take(size)
            .Select(a => new AuditDto(a.Id, a.ActorUserId, a.Action, a.EntityName, a.EntityId, a.Data, a.CreatedAt)).ToListAsync(ct);
        return new PagedDto<AuditDto>(items, total, number, size);
    }

    /// <summary>Finance-only subset of the rules: prices and billing switches.</summary>
    public async Task<Result> SavePricingAsync(PricingDto dto, CancellationToken ct)
    {
        if (new[] { dto.SelectionFee, dto.EarlyAccessMonthly, dto.BannerDaily, dto.FeaturedWeekly, dto.SponsoredOffer, dto.MinTopUp }.Any(v => v < 0)
            || dto.FreeSelections < 0 || dto.EarlyAccessDays < 1 || dto.TopUpMethods.Count == 0 || dto.TopUpMethods.Any(string.IsNullOrWhiteSpace))
            return Error.Validation("pricing.invalid", "Prices must be non-negative and at least one top-up method is required");
        var r = await rulesProvider.GetAsync(ct);
        r.BillingEnabled = dto.BillingEnabled; r.SelectionFee = dto.SelectionFee; r.FreeSelections = dto.FreeSelections;
        r.EarlyAccessMonthly = dto.EarlyAccessMonthly; r.EarlyAccessDays = dto.EarlyAccessDays; r.BannerDaily = dto.BannerDaily;
        r.FeaturedWeekly = dto.FeaturedWeekly; r.SponsoredOffer = dto.SponsoredOffer; r.MinTopUp = dto.MinTopUp; r.TopUpMethods = dto.TopUpMethods.Select(m => m.Trim()).ToArray();
        await rulesProvider.UpdateAsync(r, ct);
        s.Audit("settings.pricing", "Settings", 1, JsonSerializer.Serialize(dto));
        await Db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<IReadOnlyList<AdminAccountDto>> AdminsAsync(CancellationToken ct) =>
        await Db.Users.Where(u => u.Role == UserRole.Admin && u.DeletedAt == null).OrderBy(u => u.CreatedAt)
            .Select(u => new AdminAccountDto(u.Id, u.Email!, u.Name, u.AdminRole.ToString(), u.TotpEnabled, u.CreatedAt)).ToListAsync(ct);

    public async Task<Result<Guid>> CreateAdminAsync(CreateAdminDto dto, CancellationToken ct)
    {
        var email = dto.Email?.Trim().ToLowerInvariant() ?? "";
        if (!System.Net.Mail.MailAddress.TryCreate(email, out _)) return Error.Validation("admin.email", "Valid email is required");
        if (!Enum.TryParse<AdminRole>(dto.AdminRole, true, out var role)) return Errors.BadEnum("adminRole");
        if ((dto.Password ?? "").Length < authOptions.Value.MinPasswordLength) return Error.Validation("admin.password", "Password is too short");
        if (await Db.Users.AnyAsync(u => u.Email == email, ct)) return Error.Conflict("admin.email", "Email already used");
        var u = new User { Role = UserRole.Admin, AdminRole = role, Email = email, Name = dto.Name?.Trim(), PasswordHash = passwords.Hash(dto.Password!), PhoneHash = $"admin:{email}" };
        Db.Users.Add(u);
        s.Audit("admin.create", nameof(User), u.Id, JsonSerializer.Serialize(new { email, role = role.ToString() }));
        await Db.SaveChangesAsync(ct);
        return u.Id;
    }

    private async Task<Result<User>> OtherAdminAsync(Guid id, CancellationToken ct)
    {
        if (id == s.Me.UserId) return Error.Conflict("admin.self", "You cannot change your own admin account");
        return await Db.Users.FirstOrDefaultAsync(u => u.Id == id && u.Role == UserRole.Admin, ct) is { } u ? u : Error.NotFound("admin");
    }

    public async Task<Result> SetAdminRoleAsync(Guid id, AdminRoleDto dto, CancellationToken ct)
    {
        if (!Enum.TryParse<AdminRole>(dto.AdminRole, true, out var role)) return Errors.BadEnum("adminRole");
        var res = await OtherAdminAsync(id, ct);
        if (!res.IsSuccess) return res.Error!;
        res.Value!.AdminRole = role;
        await RevokeSessionsAsync(id, ct);
        s.Audit("admin.role", nameof(User), id, JsonSerializer.Serialize(dto));
        await Db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<Result> ResetAdminTotpAsync(Guid id, CancellationToken ct)
    {
        var res = await OtherAdminAsync(id, ct);
        if (!res.IsSuccess) return res.Error!;
        var u = res.Value!;
        u.TotpSecret = null; u.TotpEnabled = false; u.TotpLastStep = 0;
        await RevokeSessionsAsync(id, ct);
        s.Audit("admin.totp_reset", nameof(User), id);
        await Db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<Result> SetAdminDisabledAsync(Guid id, bool disabled, CancellationToken ct)
    {
        var res = await OtherAdminAsync(id, ct);
        if (!res.IsSuccess) return res.Error!;
        res.Value!.IsBanned = disabled;
        if (disabled) await RevokeSessionsAsync(id, ct);
        s.Audit(disabled ? "admin.disable" : "admin.enable", nameof(User), id);
        await Db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    private Task RevokeSessionsAsync(Guid userId, CancellationToken ct) =>
        Db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ExecuteUpdateAsync(x => x.SetProperty(t => t.RevokedAt, s.Now), ct);
}
