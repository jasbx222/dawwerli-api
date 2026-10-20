using Dawwerli.Application.Abstractions;
using Dawwerli.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dawwerli.Application.Services;

/// <summary>
/// Timer-driven transitions (early window end, 15-min widening, 24-h expiry, delivery checks, ad billing).
/// Called periodically by the background worker; every step is idempotent.
/// </summary>
public sealed class LifecycleService(Shared s, DispatchService dispatch, AdsService ads, INotifier notifier, ITextCatalog texts, IRealtime realtime)
{
    private IAppDbContext Db => s.Db;

    public async Task TickAsync(CancellationToken ct)
    {
        var rules = await s.Rules(ct);
        var now = s.Now;

        foreach (var r in await Db.Requests.Where(r => !r.PublicDispatched && r.EarlyUntil <= now && (r.Status == RequestStatus.Open || r.Status == RequestStatus.Expanded)).ToListAsync(ct))
        {
            await dispatch.DispatchPublicAsync(r, ct);
            await Db.SaveChangesAsync(ct);
        }

        foreach (var r in await Db.Requests.Where(r => r.Status == RequestStatus.Open && r.OffersCount == 0 && r.ExpandsAt <= now).ToListAsync(ct))
        {
            r.WidenedTo = await Db.GovernorateAdjacency.Where(a => a.GovernorateId == r.GovernorateId).Select(a => a.NeighborId).ToArrayAsync(ct);
            r.Status = RequestStatus.Expanded;
            await Db.SaveChangesAsync(ct);
            await dispatch.DispatchPublicAsync(r, ct);
            await Db.SaveChangesAsync(ct);
            await notifier.NotifyAsync(r.CustomerId, texts.Get("customer.widened.title"), texts.Get("customer.widened.body"), $"/live/{r.Id}", ct);
        }

        foreach (var r in await Db.Requests.Where(r => (r.Status == RequestStatus.Open || r.Status == RequestStatus.Expanded) && r.OffersCount == 0 && r.ExpiresAt <= now).ToListAsync(ct))
        {
            r.Status = RequestStatus.Expired;
            await Db.SaveChangesAsync(ct);
            await notifier.NotifyAsync(r.CustomerId, texts.Get("customer.expired.title"), texts.Get("customer.expired.body"), $"/live/{r.Id}", ct);
            await realtime.RequestChangedAsync(r.Id, ct);
        }

        // Hidden-but-recoverable offers of a selected request expire after the recovery window.
        var recoveryCut = now.AddHours(-rules.OfferRecoveryHours);
        await Db.Offers.Where(o => o.Status == OfferStatus.NotSelected && o.Request!.Status == RequestStatus.Selected
                && Db.Orders.Any(x => x.RequestId == o.RequestId && x.SelectedAt < recoveryCut))
            .ExecuteUpdateAsync(x => x.SetProperty(o => o.Status, OfferStatus.Closed), ct);

        // «وصلك؟» after the promised delivery time, re-ask after N days, close after M days.
        var awaiting = await Db.Orders.Include(o => o.Offer).Include(o => o.Page)
            .Where(o => o.Status == OrderStatus.AwaitingDelivery && o.ContactedAt != null).ToListAsync(ct);
        foreach (var o in awaiting)
        {
            var due = o.ContactedAt!.Value.AddHours(o.Offer!.DeliveryHours);
            if (now >= due.AddDays(rules.DeliveryCloseDays)) { o.Status = OrderStatus.Closed; continue; }
            var ask = o.DeliveryAskedAt is null ? now >= due : now >= o.DeliveryAskedAt.Value.AddDays(rules.DeliveryReaskDays) && o.DeliveryAskedAt < due.AddDays(rules.DeliveryReaskDays);
            if (!ask) continue;
            o.DeliveryAskedAt = now;
            await notifier.NotifyAsync(o.CustomerId, texts.Get("customer.askReceived.title"), texts.Get("customer.askReceived.body", o.Page!.Name), $"/order/{o.Id}", ct);
        }
        await Db.SaveChangesAsync(ct);

        await Db.Pages.Where(p => p.SuspendedUntil != null && p.SuspendedUntil <= now)
            .ExecuteUpdateAsync(x => x.SetProperty(p => p.SuspendedUntil, (DateTime?)null), ct);
        await Db.AdCampaigns.Where(a => a.Status == AdStatus.PausedSuspended && a.Page!.Status == PageStatus.Verified && a.Page.SuspendedUntil == null)
            .ExecuteUpdateAsync(x => x.SetProperty(a => a.Status, AdStatus.Active), ct);
        await ads.ChargeDailyAsync(ct);
    }
}
