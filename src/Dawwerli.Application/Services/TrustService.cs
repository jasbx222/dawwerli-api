using Dawwerli.Application.Abstractions;
using Dawwerli.Application.Contracts;
using Dawwerli.Domain.Common;
using Dawwerli.Domain.Entities;
using Dawwerli.Domain.Rules;
using Microsoft.EntityFrameworkCore;

namespace Dawwerli.Application.Services;

/// <summary>Reports, blocks, strikes, page marks and disputes/appeals.</summary>
public sealed class TrustService(Shared s, INotifier notifier, ITextCatalog texts)
{
    private IAppDbContext Db => s.Db;

    public async Task AddPageMarkAsync(Page page, PageMarkKind kind, Guid? orderId, string title, BusinessRules rules, CancellationToken ct)
    {
        Db.PageMarks.Add(new PageMark { PageId = page.Id, Kind = kind, OrderId = orderId, Title = title, CreatedAt = s.Now });
        var since = s.Now.AddDays(-rules.MarksWindowDays);
        var count = 1 + await Db.PageMarks.CountAsync(m => m.PageId == page.Id && m.Kind == kind && !m.UnderReview && m.CreatedAt >= since, ct);
        var suspend = kind == PageMarkKind.NoReply ? TrustPolicy.ShouldSuspendForMarks(count, rules) : count >= rules.ComplaintsForSuspension;
        if (kind == PageMarkKind.Complaint && count >= rules.ComplaintsForBan) page.Status = PageStatus.Banned;
        else if (suspend) page.SuspendedUntil = s.Now.AddDays(rules.SuspensionDays);
        if (page.Status == PageStatus.Banned || page.SuspendedUntil > s.Now)
        {
            await Db.AdCampaigns.Where(a => a.PageId == page.Id && a.Status == AdStatus.Active)
                .ExecuteUpdateAsync(x => x.SetProperty(a => a.Status, AdStatus.PausedSuspended), ct);
            await notifier.NotifyAsync(page.UserId, texts.Get("page.suspended.title"), texts.Get("page.suspended.body"), "/seller/standing", ct);
        }
    }

    public async Task AddStrikeAsync(Guid userId, string reason, Guid? orderId, BusinessRules rules, CancellationToken ct)
    {
        Db.Strikes.Add(new Strike { UserId = userId, Reason = reason, OrderId = orderId, CreatedAt = s.Now });
        var since = s.Now.AddDays(-rules.StrikeWindowDays);
        var count = 1 + await Db.Strikes.CountAsync(x => x.UserId == userId && !x.Frozen && x.CreatedAt >= since, ct);
        if (!TrustPolicy.ShouldBan(count, rules)) return;
        var u = await Db.Users.FirstAsync(x => x.Id == userId, ct);
        if (TrustPolicy.IsPermanent(u.BanCount, rules)) u.IsBanned = true;
        else u.PostingBannedUntil = s.Now.AddDays(rules.PostingBanDays);
        u.BanCount++;
        await notifier.NotifyAsync(userId, texts.Get("customer.banned.title"), texts.Get("customer.banned.body"), "/standing", ct);
    }

    public async Task<Result<IdDto>> ReportAsync(ReportDto dto, CancellationToken ct)
    {
        if (!EnumText.TryParse<ReportTarget>(dto.Target, out var target)) return Errors.BadEnum("target");
        if (!EnumText.TryParse<ReportReason>(dto.Reason, out var reason)) return Errors.BadEnum("reason");
        var exists = target == ReportTarget.Page
            ? await Db.Pages.AnyAsync(p => p.Id == dto.TargetId, ct)
            : await Db.Users.AnyAsync(u => u.Id == dto.TargetId, ct);
        if (!exists) return Error.NotFound(target.Code());
        var report = new Report
        {
            ReporterUserId = s.Me.UserId, Target = target, TargetId = dto.TargetId, OrderId = dto.OrderId, OfferId = dto.OfferId,
            Reason = reason, Text = dto.Text?.Trim() ?? "",
            Status = reason == ReportReason.DepositRequest ? CaseStatus.UnderReview : CaseStatus.Open,
        };
        Db.Reports.Add(report);
        await Db.SaveChangesAsync(ct);
        return new IdDto(report.Id);
    }

    public async Task<Result> BlockAsync(BlockDto dto, CancellationToken ct)
    {
        var blocked = dto.UserId ?? await Db.Pages.Where(p => p.Id == dto.PageId).Select(p => (Guid?)p.UserId).FirstOrDefaultAsync(ct);
        if (blocked is null || blocked == s.Me.UserId) return Error.NotFound("user");
        if (!await Db.Blocks.AnyAsync(b => b.BlockerUserId == s.Me.UserId && b.BlockedUserId == blocked, ct))
            Db.Blocks.Add(new Block { BlockerUserId = s.Me.UserId, BlockedUserId = blocked.Value });
        await Db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task OpenSystemDisputeAsync(Order order, DisputeType type, string text, CancellationToken ct)
    {
        var rules = await s.Rules(ct);
        Db.Disputes.Add(new Dispute
        {
            OpenedByUserId = s.Me.UserId, OrderId = order.Id, Type = type, Text = text, Status = CaseStatus.Open,
            DecideBy = s.Now.AddHours(rules.DisputeDecisionHours),
        });
    }

    public async Task<Result<DisputeViewDto>> CreateDisputeAsync(CreateDisputeDto dto, CancellationToken ct)
    {
        if (!EnumText.TryParse<DisputeType>(dto.Type, out var type)) return Errors.BadEnum("type");
        if (ContactFilter.HasContact(dto.Text)) return Error.Rule("dispute.contact", "Remove contact info");
        var rules = await s.Rules(ct);
        var me = s.Me.UserId;
        if (dto.OrderId is { } oid)
        {
            var order = await Db.Orders.Include(o => o.Page).FirstOrDefaultAsync(o => o.Id == oid, ct);
            if (order is null) return Error.NotFound("order");
            if (order.CustomerId != me && order.Page!.UserId != me) return Errors.NotOwner;
            if (await Db.Disputes.AnyAsync(d => d.OrderId == oid && d.OpenedByUserId == me &&
                    (d.Status == CaseStatus.Open || d.Status == CaseStatus.UnderReview), ct))
                return Error.Conflict("dispute.exists", "A dispute is already open for this order");
        }
        if (type == DisputeType.StrikeAppeal && dto.SubjectId is { } sid)
        {
            await Db.Strikes.Where(x => x.Id == sid && x.UserId == me).ExecuteUpdateAsync(x => x.SetProperty(k => k.Frozen, true), ct);
            var myPageId = await Db.Pages.Where(p => p.UserId == me).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);
            await Db.PageMarks.Where(m => m.Id == sid && m.PageId == myPageId).ExecuteUpdateAsync(x => x.SetProperty(k => k.UnderReview, true), ct);
        }
        var d = new Dispute
        {
            OpenedByUserId = me, OrderId = dto.OrderId, SubjectId = dto.SubjectId, Type = type, Text = dto.Text?.Trim() ?? "",
            Photos = dto.Photos?.ToArray() ?? [], Status = CaseStatus.Open, DecideBy = s.Now.AddHours(rules.DisputeDecisionHours),
        };
        Db.Disputes.Add(d);
        await Db.SaveChangesAsync(ct);
        return ToDto(d);
    }

    public async Task<Result<DisputeViewDto>> DisputeAsync(Guid id, CancellationToken ct)
    {
        var d = await Db.Disputes.FirstOrDefaultAsync(x => x.Id == id && x.OpenedByUserId == s.Me.UserId, ct);
        return d is null ? Error.NotFound("dispute") : ToDto(d);
    }

    public async Task<IReadOnlyList<DisputeViewDto>> MyDisputesAsync(CancellationToken ct) =>
        (await Db.Disputes.Where(x => x.OpenedByUserId == s.Me.UserId).OrderByDescending(x => x.CreatedAt).ToListAsync(ct)).Select(ToDto).ToList();

    public static DisputeViewDto ToDto(Dispute d) => new(d.Id, d.OrderId, d.Type.Code(), d.Status.Code(), d.Text, d.Decision, d.CreatedAt, d.DecideBy);
}
