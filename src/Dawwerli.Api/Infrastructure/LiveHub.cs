using Dawwerli.Application.Abstractions;
using Dawwerli.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Dawwerli.Api.Infrastructure;

/// <summary>Realtime: clients join request:{id} (owner) or page:{id} (own page) and get "changed" pings to refetch.</summary>
[Authorize]
public sealed class LiveHub(AppDbContext db, ICurrentUser me) : Hub
{
    public const string Path = "/hubs/live";

    public async Task WatchRequest(Guid requestId)
    {
        if (await db.Requests.AnyAsync(r => r.Id == requestId && r.CustomerId == me.UserId))
            await Groups.AddToGroupAsync(Context.ConnectionId, $"request:{requestId}");
    }

    public async Task WatchInbox()
    {
        var page = await db.Pages.Where(p => p.UserId == me.UserId).Select(p => (Guid?)p.Id).FirstOrDefaultAsync();
        if (page is not null) await Groups.AddToGroupAsync(Context.ConnectionId, $"page:{page}");
    }
}

public sealed class SignalRRealtime(IHubContext<LiveHub> hub) : IRealtime
{
    public Task RequestChangedAsync(Guid requestId, CancellationToken ct = default) =>
        hub.Clients.Group($"request:{requestId}").SendAsync("changed", requestId, ct);

    public Task PageInboxChangedAsync(Guid pageId, CancellationToken ct = default) =>
        hub.Clients.Group($"page:{pageId}").SendAsync("inbox", pageId, ct);
}
