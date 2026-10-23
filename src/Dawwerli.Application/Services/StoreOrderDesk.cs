using System.Text.Json;
using Dawwerli.Application.Abstractions;
using Dawwerli.Application.Contracts;
using Dawwerli.Domain.Common;
using Dawwerli.Domain.Entities;
using Dawwerli.Domain.Rules;
using Microsoft.EntityFrameworkCore;

namespace Dawwerli.Application.Services;

/// <summary>
/// Store-order handling for one page, shared by the web seller portal and the page app.
/// Every query is scoped to the given page id, so a page only ever sees and moves its own orders.
/// </summary>
public sealed class StoreOrderDesk(StoreKit kit, Shared s, INotifier notifier, ITextCatalog texts, IPhoneProtector phones, OfferOrderLink link,
    CourierTrackingService tracking)
{
    private IAppDbContext Db => kit.Db;

    /// <summary>Open orders the page still has to act on.</summary>
    public const string ActiveGroup = "active";
    /// <summary>Finished orders (delivered, cancelled or rejected).</summary>
    public const string ClosedGroup = "closed";

    private static readonly StoreOrderStatus[] Active = [StoreOrderStatus.New, StoreOrderStatus.Accepted, StoreOrderStatus.OutForDelivery];

    public static bool IsActive(StoreOrderStatus st) => Active.Contains(st);

    /// <param name="allowGroups">Also accept "active" / "closed" as the status filter.</param>
    /// <remarks>List rows never carry the customer phone: it is shown only inside one order's details.</remarks>
    public async Task<Result<PagedDto<SellerOrderDto>>> ListAsync(Guid pageId, string? status, int? pageNo, int? size,
        bool allowGroups, CancellationToken ct)
    {
        var (p, n) = kit.Paging(pageNo, size);
        var q = kit.Orders.Where(o => o.PageId == pageId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            var key = status.Trim();
            if (allowGroups && key.Equals(ActiveGroup, StringComparison.OrdinalIgnoreCase)) q = q.Where(o => Active.Contains(o.Status));
            else if (allowGroups && key.Equals(ClosedGroup, StringComparison.OrdinalIgnoreCase)) q = q.Where(o => !Active.Contains(o.Status));
            else if (EnumText.TryParse<StoreOrderStatus>(key, out var st)) q = q.Where(o => o.Status == st);
            else return Errors.BadEnum("status");
        }
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Number).Skip((p - 1) * n).Take(n).ToListAsync(ct);
        var ids = rows.Select(r => r.CustomerUserId).Distinct().ToList();
        var users = await Db.Users.Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
        var deals = await kit.DealsAsync(rows, ct);
        return new PagedDto<SellerOrderDto>(rows.Select(o => new SellerOrderDto(kit.ToDto(o, deals), users.GetValueOrDefault(o.CustomerUserId)?.Name, null)).ToList(), total, p, n);
    }

    public async Task<Result<SellerOrderDto>> GetAsync(Guid pageId, Guid id, bool hideClosedPhone, CancellationToken ct)
    {
        var o = await kit.Orders.FirstOrDefaultAsync(x => x.Id == id && x.PageId == pageId, ct);
        if (o is null) return StoreErrors.OrderNotFound;
        return await DetailsAsync(o, hideClosedPhone, ct);
    }

    public async Task<Result<SellerOrderDto>> MoveAsync(Guid pageId, Guid actorId, Guid id, SetOrderStatusDto dto, bool hideClosedPhone, CancellationToken ct)
    {
        if (!EnumText.TryParse<StoreOrderStatus>(dto.Status, out var to)) return Errors.BadEnum("status");
        await using var tx = await Db.BeginTransactionAsync(ct);
        await Db.LockStoreOrderAsync(id, ct);
        var o = await kit.Orders.FirstOrDefaultAsync(x => x.Id == id && x.PageId == pageId, ct);
        if (o is null) return StoreErrors.OrderNotFound;
        if (!StoreOrderFlow.SellerCanMove(o.Status, to)) return StoreErrors.BadTransition;
        if (OrderPin.Check(null, null, dto.Reason) is { } leak) return leak;
        Courier? courier = null;
        if (to == StoreOrderStatus.OutForDelivery && dto.CourierId is { } cid)
        {
            courier = await Db.Couriers.FirstOrDefaultAsync(c => c.Id == cid && c.PageId == pageId && c.DeletedAt == null, ct);
            if (courier is null) return Error.NotFound("courier");
            if (!courier.Active) return Error.Rule("courier.inactive", "هذا المندوب موقوف، فعّله أو اختار غيره");
        }
        if (o.Kind == StoreOrderKind.Offer && await link.OnPageMoveAsync(o, to, ct) is { } dealError) return dealError;
        o.Record(to, s.Now);
        o.StatusReason = dto.Reason?.Trim();
        if (courier is not null)
        {
            o.CourierId = courier.Id;
            o.CourierName = courier.Name;
            o.CourierPhone = courier.Phone;
        }
        if (o.Kind == StoreOrderKind.Store && StoreOrderFlow.RestoresStock(to)) await kit.RestoreStockAsync(o, ct);
        // A courier link is issued when the order goes out with a courier and ends with the trip.
        if (to == StoreOrderStatus.OutForDelivery && courier is not null) await tracking.IssueAsync(o, ct);
        else if (to != StoreOrderStatus.OutForDelivery) await tracking.EndForOrderAsync(o.Id, ct);
        Db.AuditLogs.Add(new AuditLog { ActorUserId = actorId, Action = "store_order.status", EntityName = nameof(StoreOrder),
            EntityId = o.Id.ToString(), Data = JsonSerializer.Serialize(new { to = to.Code(), courier = courier?.Id }) });
        await Db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        var body = to switch
        {
            StoreOrderStatus.OutForDelivery when o.CourierName is not null => texts.Get("store.courier.body", o.CourierName, o.CourierPhone ?? ""),
            StoreOrderStatus.Delivered when o.Kind == StoreOrderKind.Offer => texts.Get("store.inspect.body"),
            _ => texts.Get($"store.status.{to.Code()}"),
        };
        await notifier.NotifyAsync(o.CustomerUserId, texts.Get("store.status.title", Shared.OrderNo(o.Number)), body, $"/store/orders/{o.Id}", ct);
        if (o.Kind == StoreOrderKind.Offer && to is StoreOrderStatus.Rejected or StoreOrderStatus.Cancelled) await link.NotifyReopenedAsync(o, ct);
        return await DetailsAsync(o, hideClosedPhone, ct);
    }

    /// <summary>
    /// The one place the customer phone is exposed to a page: the details of an order that belongs to that page
    /// (every caller has already scoped the order to the page id).
    /// </summary>
    private async Task<SellerOrderDto> DetailsAsync(StoreOrder o, bool hideClosedPhone, CancellationToken ct)
    {
        var u = await Db.Users.FirstOrDefaultAsync(x => x.Id == o.CustomerUserId, ct);
        var deal = o.OfferOrderId is { } did ? await Db.Orders.FirstOrDefaultAsync(x => x.Id == did, ct) : null;
        var showPhone = u is not null && !string.IsNullOrEmpty(u.PhoneEncrypted) && (!hideClosedPhone || IsActive(o.Status));
        var link = await tracking.LinkAsync(o, ct);
        return new(kit.ToDto(o, deal), u?.Name, showPhone ? phones.Decrypt(u!.PhoneEncrypted) : null, link?.Url, link);
    }
}

/// <summary>The signed-in page's own store orders, for the page app. Phones are shown only while an order is still open.</summary>
public sealed class PageStoreOrderService(StoreOrderDesk desk, Shared s, CourierTrackingService tracking)
{
    public async Task<Result<CourierLocationDto>> CourierLocationAsync(Guid id, CancellationToken ct) =>
        await s.MyPageAsync(ct) is { } page ? await tracking.ForPageAsync(page.Id, id, ct) : Errors.NotPage;

    public async Task<Result<CourierTrackingLinkDto>> RegenerateLinkAsync(Guid id, CancellationToken ct) =>
        await s.MyPageAsync(ct) is { } page ? await tracking.RegenerateAsync(page.Id, id, ct) : Errors.NotPage;

    /// <summary>Hands an order that is already out to another courier; the old courier link stops working.</summary>
    public async Task<Result<SellerOrderDto>> ReassignCourierAsync(Guid id, AssignCourierDto dto, CancellationToken ct)
    {
        var page = await s.MyPageAsync(ct);
        if (page is null) return Errors.NotPage;
        var r = await tracking.ReassignAsync(page.Id, id, dto.CourierId, ct);
        if (!r.IsSuccess) return r.Error!;
        return await desk.GetAsync(page.Id, id, hideClosedPhone: true, ct);
    }

    public async Task<Result<PagedDto<SellerOrderDto>>> ListAsync(string? status, int? pageNo, int? size, CancellationToken ct)
    {
        var page = await s.MyPageAsync(ct);
        if (page is null) return Errors.NotPage;
        if (pageNo is < 1 || size is < 1) return Error.Validation("paging.invalid", "Page and page size start at 1");
        return await desk.ListAsync(page.Id, status, pageNo, size, allowGroups: true, ct);
    }

    public async Task<Result<SellerOrderDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var page = await s.MyPageAsync(ct);
        if (page is null) return Errors.NotPage;
        return await desk.GetAsync(page.Id, id, hideClosedPhone: true, ct);
    }

    public async Task<Result<SellerOrderDto>> SetStatusAsync(Guid id, SetOrderStatusDto dto, CancellationToken ct)
    {
        var page = await s.MyPageAsync(ct);
        if (page is null) return Errors.NotPage;
        return await desk.MoveAsync(page.Id, s.Me.UserId, id, dto, hideClosedPhone: true, ct);
    }
}
