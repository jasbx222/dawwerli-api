using Dawwerli.Application.Abstractions;
using Dawwerli.Domain.Entities;
using Dawwerli.Domain.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Dawwerli.Application.Services;

/// <summary>Finds matching pages for a request and notifies them (early-access pages first).</summary>
public sealed class DispatchService(IAppDbContext db, IClock clock, INotifier notifier, IRealtime realtime, ITextCatalog texts,
    IOptions<RegionOptions> region)
{
    private readonly TimeZoneInfo _tz = TimeZoneInfo.FindSystemTimeZoneById(region.Value.TimeZone);

    public async Task<List<Page>> MatchingPagesAsync(Request r, bool earlyOnly, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var govs = r.WidenedTo.Append(r.GovernorateId).ToArray();
        var blocked = db.Blocks.Where(b => b.BlockerUserId == r.CustomerId || b.BlockedUserId == r.CustomerId)
            .Select(b => b.BlockerUserId == r.CustomerId ? b.BlockedUserId : b.BlockerUserId);
        var already = db.RequestDispatches.Where(d => d.RequestId == r.Id).Select(d => d.PageId);
        var q = db.Pages.Where(p => p.Status == PageStatus.Verified && (p.SuspendedUntil == null || p.SuspendedUntil < now) && p.Receiving
            && p.UserId != r.CustomerId
            && p.Categories.Any(c => c.CategoryId == r.CategoryId)
            && p.Governorates.Any(g => govs.Contains(g.GovernorateId))
            && !blocked.Contains(p.UserId) && !already.Contains(p.Id));
        if (earlyOnly) q = q.Where(p => p.EarlyAccessUntil > now);
        var local = TimeZoneInfo.ConvertTimeFromUtc(now, _tz);
        return (await q.ToListAsync(ct)).Where(p => InWorkingHours(p, local)).ToList();
    }

    private static bool InWorkingHours(Page p, DateTime local)
    {
        if (p.WorkDays.Length > 0 && !p.WorkDays.Contains((int)local.DayOfWeek)) return false;
        if (p.WorkFrom == p.WorkTo) return true;
        var h = local.Hour;
        return p.WorkFrom < p.WorkTo ? h >= p.WorkFrom && h < p.WorkTo : h >= p.WorkFrom || h < p.WorkTo;
    }

    /// <summary>Called on publish: early-access pages get it now; others after the early window (or now if none).</summary>
    public async Task<int> PublishAsync(Request r, BusinessRules rules, CancellationToken ct)
    {
        var early = await MatchingPagesAsync(r, earlyOnly: true, ct);
        if (early.Count == 0)
        {
            r.EarlyUntil = clock.UtcNow;
            return await DispatchPublicAsync(r, ct);
        }
        r.EarlyUntil = clock.UtcNow.AddSeconds(rules.EarlyWindowSec);
        return await DispatchAsync(r, early, ct);
    }

    public async Task<int> DispatchPublicAsync(Request r, CancellationToken ct)
    {
        r.PublicDispatched = true;
        return await DispatchAsync(r, await MatchingPagesAsync(r, earlyOnly: false, ct), ct);
    }

    private async Task<int> DispatchAsync(Request r, List<Page> pages, CancellationToken ct)
    {
        var now = clock.UtcNow;
        foreach (var p in pages) db.RequestDispatches.Add(new RequestDispatch { RequestId = r.Id, PageId = p.Id, DispatchedAt = now });
        await db.SaveChangesAsync(ct);
        foreach (var p in pages)
        {
            await notifier.NotifyAsync(p.UserId, texts.Get("page.newRequest.title"), texts.Get("page.newRequest.body", r.Title), $"/seller/requests/{r.Id}/offer", ct);
            await realtime.PageInboxChangedAsync(p.Id, ct);
        }
        await realtime.RequestChangedAsync(r.Id, ct);
        return pages.Count;
    }
}
