using Dawwerli.Application.Abstractions;
using Dawwerli.Application.Contracts;
using Dawwerli.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dawwerli.Application.Services;

public sealed class NotificationService(Shared s)
{
    public async Task<CursorPage<NotificationDto>> ListAsync(string? cursor, int take, CancellationToken ct)
    {
        var q = ForRole();
        if (DateTime.TryParse(cursor, null, System.Globalization.DateTimeStyles.RoundtripKind, out var before)) q = q.Where(n => n.CreatedAt < before);
        var rows = await q.OrderByDescending(n => n.CreatedAt).Take(take).ToListAsync(ct);
        return new CursorPage<NotificationDto>(rows.Select(n => new NotificationDto(n.Id, n.Title, n.Body, n.Route, n.CreatedAt, n.ReadAt != null)).ToList(),
            rows.Count == take ? rows[^1].CreatedAt.ToString("O") : null);
    }

    public async Task ReadAllAsync(CancellationToken ct)
    {
        var now = s.Now;
        await ForRole().Where(n => n.ReadAt == null).ExecuteUpdateAsync(x => x.SetProperty(n => n.ReadAt, now), ct);
    }

    /// <summary>Seller notices use /seller routes. The customer list is everything else, so one account does not see both.</summary>
    private IQueryable<Notification> ForRole()
    {
        var q = s.Db.Notifications.Where(n => n.UserId == s.Me.UserId);
        return s.Me.Role == UserRole.Page
            ? q.Where(n => n.Route.StartsWith("/seller"))
            : q.Where(n => !n.Route.StartsWith("/seller"));
    }
}
