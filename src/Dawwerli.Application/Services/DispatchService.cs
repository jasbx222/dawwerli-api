using Dawwerli.Application.Abstractions;
using Dawwerli.Domain.Entities;
using Dawwerli.Domain.Rules;
using Microsoft.EntityFrameworkCore;

namespace Dawwerli.Application.Services;

/// <summary>Sends a published request to every page. Who can submit an offer is decided at submit time.</summary>
public sealed class DispatchService(IAppDbContext db, IClock clock, INotifier notifier, IRealtime realtime, ITextCatalog texts)
{
    public async Task<List<Page>> MatchingPagesAsync(Request r, CancellationToken ct)
    {
        var already = db.RequestDispatches.Where(d => d.RequestId == r.Id).Select(d => d.PageId);
        return await db.Pages.Where(p => !already.Contains(p.Id)).ToListAsync(ct);
    }

    public async Task<int> PublishAsync(Request r, BusinessRules rules, CancellationToken ct)
    {
        _ = rules;
        r.EarlyUntil = clock.UtcNow;
        return await DispatchPublicAsync(r, ct);
    }

    /// <summary>A request made inside one page: that page sees it, and the customer gets a different notice.</summary>
    public async Task<int> PublishToPageAsync(Request r, Page page, CancellationToken ct)
    {
        r.EarlyUntil = clock.UtcNow;
        r.PublicDispatched = true;
        var sent = await DispatchAsync(r, [page], ct);
        await notifier.NotifyAsync(r.CustomerId, texts.Get("customer.requestSent.title"), texts.Get("customer.requestSent.body", page.Name),
            $"/requests/{r.Id}/live", ct);
        return sent;
    }

    public async Task<int> DispatchPublicAsync(Request r, CancellationToken ct)
    {
        r.PublicDispatched = true;
        return await DispatchAsync(r, await MatchingPagesAsync(r, ct), ct);
    }

    private async Task<int> DispatchAsync(Request r, List<Page> pages, CancellationToken ct)
    {
        var now = clock.UtcNow;
        foreach (var p in pages) db.RequestDispatches.Add(new RequestDispatch { RequestId = r.Id, PageId = p.Id, DispatchedAt = now });
        await db.SaveChangesAsync(ct);
        foreach (var p in pages)
        {
            var direct = r.Fields.Contains(p.Id.ToString(), StringComparison.OrdinalIgnoreCase);
            var title = texts.Get(direct ? "page.directRequest.title" : "page.newRequest.title");
            var body = texts.Get(direct ? "page.directRequest.body" : "page.newRequest.body", r.Title);
            var route = direct ? "/seller/offers" : $"/seller/requests/{r.Id}/offer";
            await notifier.NotifyAsync(p.UserId, title, body, route, ct);
            await realtime.PageInboxChangedAsync(p.Id, ct);
        }
        await realtime.RequestChangedAsync(r.Id, ct);
        return pages.Count;
    }
}
