using System.Security.Cryptography;
using Dawwerli.Application.Abstractions;
using Dawwerli.Application.Contracts;
using Dawwerli.Domain.Common;
using Dawwerli.Domain.Entities;
using Dawwerli.Domain.Rules;
using Microsoft.EntityFrameworkCore;

namespace Dawwerli.Application.Services;

/// <summary>
/// Courier live tracking through a link the page sends to its courier (no courier app or login).
/// A link works only while its order is out for delivery with the same courier. Only the latest position is kept,
/// and it is wiped as soon as tracking ends. The customer and the owning page can read the position; nobody else.
/// </summary>
public sealed class CourierTrackingService(Shared s, StoreKit kit, IPhoneProtector phones, IRealtime realtime, ITrackingLinks links)
{
    private IAppDbContext Db => s.Db;

    /// <summary>Rough urban courier speed used for the ETA (km/h), with a detour factor on the straight line.</summary>
    private const double CitySpeedKmh = 20, DetourFactor = 1.3;

    public static readonly Error LinkNotFound = Error.NotFound("tracking");
    private static readonly Error NoCourier = Error.Rule("tracking.no_courier", "حدد مندوب للطلب وهو بالطريق حتى يطلع رابط التتبع");

    public static string NewToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string Base64Url(byte[] b) => Convert.ToBase64String(b).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Revokes the order's current link (clearing its position) and adds a fresh one for the order's courier. Caller saves.</summary>
    public async Task<CourierTracking> IssueAsync(StoreOrder o, CancellationToken ct)
    {
        await EndForOrderAsync(o.Id, ct);
        var t = new CourierTracking { OrderId = o.Id, CourierId = o.CourierId, Token = NewToken(), CreatedAt = s.Now };
        Db.CourierTrackings.Add(t);
        return t;
    }

    /// <summary>Ends tracking for an order: every live link is revoked and its position wiped. Caller saves.</summary>
    public async Task EndForOrderAsync(Guid orderId, CancellationToken ct)
    {
        var live = await Db.CourierTrackings.Where(t => t.OrderId == orderId && t.RevokedAt == null).ToListAsync(ct);
        foreach (var t in live)
        {
            t.RevokedAt = s.Now;
            t.ClearLocation();
        }
    }

    private static bool Valid(CourierTracking t, StoreOrder o) =>
        t.RevokedAt == null && o.Status == StoreOrderStatus.OutForDelivery && o.CourierId != null && o.CourierId == t.CourierId;

    private async Task<CourierTracking?> CurrentAsync(StoreOrder o, CancellationToken ct)
    {
        var t = await Db.CourierTrackings.Where(x => x.OrderId == o.Id && x.RevokedAt == null).OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(ct);
        return t is not null && Valid(t, o) ? t : null;
    }

    // ---------- page side ----------

    /// <summary>The live link of one of the page's orders, for its order details (null when none is valid).</summary>
    public async Task<CourierTrackingLinkDto?> LinkAsync(StoreOrder o, CancellationToken ct) =>
        await CurrentAsync(o, ct) is { } t ? new CourierTrackingLinkDto(links.Url(t.Token), t.Sharing, t.LocationAt, t.CreatedAt) : null;

    public async Task<Result<CourierTrackingLinkDto>> RegenerateAsync(Guid pageId, Guid orderId, CancellationToken ct)
    {
        await using var tx = await Db.BeginTransactionAsync(ct);
        await Db.LockStoreOrderAsync(orderId, ct);
        var o = await Db.StoreOrders.FirstOrDefaultAsync(x => x.Id == orderId && x.PageId == pageId, ct);
        if (o is null) return StoreErrors.OrderNotFound;
        if (o.Status != StoreOrderStatus.OutForDelivery || o.CourierId is null) return NoCourier;
        var t = await IssueAsync(o, ct);
        await Db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await realtime.CourierMovedAsync(o.Id, new CourierLocationDto(false), ct);
        return new CourierTrackingLinkDto(links.Url(t.Token), false, null, t.CreatedAt);
    }

    /// <summary>Changes the courier of an order that is already out: the old link stops working and a new one is issued.</summary>
    public async Task<Result> ReassignAsync(Guid pageId, Guid orderId, Guid courierId, CancellationToken ct)
    {
        await using var tx = await Db.BeginTransactionAsync(ct);
        await Db.LockStoreOrderAsync(orderId, ct);
        var o = await Db.StoreOrders.FirstOrDefaultAsync(x => x.Id == orderId && x.PageId == pageId, ct);
        if (o is null) return StoreErrors.OrderNotFound;
        if (o.Status != StoreOrderStatus.OutForDelivery) return StoreErrors.BadTransition;
        var courier = await Db.Couriers.FirstOrDefaultAsync(c => c.Id == courierId && c.PageId == pageId && c.DeletedAt == null, ct);
        if (courier is null) return Error.NotFound("courier");
        if (!courier.Active) return Error.Rule("courier.inactive", "هذا المندوب موقوف، فعّله أو اختار غيره");
        o.CourierId = courier.Id;
        o.CourierName = courier.Name;
        o.CourierPhone = courier.Phone;
        await IssueAsync(o, ct);
        Db.AuditLogs.Add(new AuditLog { ActorUserId = s.Me.UserId, Action = "store_order.courier", EntityName = nameof(StoreOrder),
            EntityId = o.Id.ToString(), Data = System.Text.Json.JsonSerializer.Serialize(new { courier = courier.Id }) });
        await Db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await realtime.CourierMovedAsync(o.Id, new CourierLocationDto(false), ct);
        return Result.Ok();
    }

    // ---------- reading the position ----------

    public async Task<Result<CourierLocationDto>> ForCustomerAsync(Guid orderId, CancellationToken ct)
    {
        var o = await Db.StoreOrders.FirstOrDefaultAsync(x => x.Id == orderId && x.CustomerUserId == s.Me.UserId, ct);
        return o is null ? StoreErrors.OrderNotFound : await LocationAsync(o, ct);
    }

    public async Task<Result<CourierLocationDto>> ForPageAsync(Guid pageId, Guid orderId, CancellationToken ct)
    {
        var o = await Db.StoreOrders.FirstOrDefaultAsync(x => x.Id == orderId && x.PageId == pageId, ct);
        return o is null ? StoreErrors.OrderNotFound : await LocationAsync(o, ct);
    }

    private async Task<CourierLocationDto> LocationAsync(StoreOrder o, CancellationToken ct) =>
        await CurrentAsync(o, ct) is { Sharing: true, Latitude: not null } t ? ToLocation(t, o) : new CourierLocationDto(false);

    private static CourierLocationDto ToLocation(CourierTracking t, StoreOrder o)
    {
        double? meters = o.Latitude is { } lat && o.Longitude is { } lng ? Math.Round(DistanceMeters(t.Latitude!.Value, t.Longitude!.Value, lat, lng)) : null;
        int? eta = meters is { } m ? EtaMinutes(m) : null;
        return new CourierLocationDto(true, t.Latitude, t.Longitude, t.Accuracy, t.Heading, t.LocationAt, meters, eta);
    }

    public static int EtaMinutes(double straightMeters) =>
        Math.Max(1, (int)Math.Ceiling(straightMeters * DetourFactor / (CitySpeedKmh * 1000 / 60)));

    public static double DistanceMeters(double lat1, double lng1, double lat2, double lng2)
    {
        const double R = 6_371_000;
        double Rad(double d) => d * Math.PI / 180;
        var dLat = Rad(lat2 - lat1);
        var dLng = Rad(lng2 - lng1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return 2 * R * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    // ---------- public courier link ----------

    private async Task<(CourierTracking T, StoreOrder O)?> ResolveAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length is < 40 or > 64) return null;
        var t = await Db.CourierTrackings.FirstOrDefaultAsync(x => x.Token == token, ct);
        if (t is null || t.RevokedAt is not null) return null;
        var o = await kit.Orders.FirstOrDefaultAsync(x => x.Id == t.OrderId, ct);
        if (o is null) return null;
        if (!Valid(t, o))
        {
            // The order moved on through a path that did not end tracking: end it now.
            t.RevokedAt = s.Now;
            t.ClearLocation();
            await Db.SaveChangesAsync(ct);
            return null;
        }
        return (t, o);
    }

    /// <summary>Order info for the courier. Includes the customer name and phone so the courier can call; only while the link is valid.</summary>
    public async Task<Result<TrackOrderDto>> GetAsync(string token, CancellationToken ct)
    {
        if (await ResolveAsync(token, ct) is not { } r) return LinkNotFound;
        var (t, o) = r;
        var customer = await Db.Users.FirstOrDefaultAsync(u => u.Id == o.CustomerUserId, ct);
        var owner = o.Page is null ? null : await Db.Users.FirstOrDefaultAsync(u => u.Id == o.Page.UserId, ct);
        string? Phone(User? u) => u is null || string.IsNullOrEmpty(u.PhoneEncrypted) ? null : phones.Decrypt(u.PhoneEncrypted);
        return new TrackOrderDto(Shared.OrderNo(o.Number), o.Status.Code(), o.Page?.Name ?? "", Phone(owner), customer?.Name, Phone(customer),
            o.CourierName ?? "", o.Address, o.Note,
            o.Latitude is { } lat && o.Longitude is { } lng ? new OrderPinDto(lat, lng, o.LocationNote) : null,
            o.Items.Select(i => new TrackItemDto(i.Name, i.Quantity)).ToList(),
            o.PaymentMethod.Code(), o.PaymentStatus == PaymentStatus.CashOnDelivery ? o.Subtotal : 0, t.Sharing, t.LocationAt);
    }

    public async Task<Result<TrackStateDto>> StartAsync(string token, CancellationToken ct)
    {
        if (await ResolveAsync(token, ct) is not { } r) return LinkNotFound;
        var t = r.T;
        if (!t.Sharing)
        {
            t.StartedAt = s.Now;
            t.StoppedAt = null;
            await Db.SaveChangesAsync(ct);
        }
        return new TrackStateDto(true, t.LocationAt);
    }

    public async Task<Result<TrackStateDto>> StopAsync(string token, CancellationToken ct)
    {
        if (await ResolveAsync(token, ct) is not { } r) return LinkNotFound;
        var t = r.T;
        t.StoppedAt = s.Now;
        t.ClearLocation();
        await Db.SaveChangesAsync(ct);
        await realtime.CourierMovedAsync(r.O.Id, new CourierLocationDto(false), ct);
        return new TrackStateDto(false, null);
    }

    public async Task<Result<TrackStateDto>> LocationAsync(string token, TrackLocationDto dto, CancellationToken ct)
    {
        if (!double.IsFinite(dto.Lat) || !double.IsFinite(dto.Lng)) return Error.Validation("location.invalid", "Invalid coordinates");
        if (await ResolveAsync(token, ct) is not { } r) return LinkNotFound;
        var (t, o) = r;
        if (!t.Sharing) return Error.Conflict("tracking.not_started", "Start sharing first");
        t.Latitude = dto.Lat;
        t.Longitude = dto.Lng;
        t.Accuracy = dto.Accuracy;
        t.Heading = dto.Heading;
        t.Speed = dto.Speed;
        t.LocationAt = s.Now;
        await Db.SaveChangesAsync(ct);
        await realtime.CourierMovedAsync(o.Id, ToLocation(t, o), ct);
        return new TrackStateDto(true, t.LocationAt);
    }
}
