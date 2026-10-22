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
public sealed class StoreOrderDesk(StoreKit kit, Shared s, INotifier notifier, ITextCatalog texts, IPhoneProtector phones)
{
    private IAppDbContext Db => kit.Db;

    /// <summary>Open orders the page still has to act on.</summary>
    public const string ActiveGroup = "active";
    /// <summary>Finished orders (delivered, cancelled or rejected).</summary>
    public const string ClosedGroup = "closed";

    private static readonly StoreOrderStatus[] Active = [StoreOrderStatus.New, StoreOrderStatus.Accepted, StoreOrderStatus.OutForDelivery];

    public static bool IsActive(StoreOrderStatus st) => Active.Contains(st);

    /// <param name="allowGroups">Also accept "active" / "closed" as the status filter.</param>
    /// <param name="hideClosedPhone">Drop the customer phone once the order is finished.</param>
    public async Task<Result<PagedDto<SellerOrderDto>>> ListAsync(Guid pageId, string? status, int? pageNo, int? size,
        bool allowGroups, bool hideClosedPhone, CancellationToken ct)
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
        return new PagedDto<SellerOrderDto>(rows.Select(o => ToSellerOrder(o, users.GetValueOrDefault(o.CustomerUserId), hideClosedPhone)).ToList(), total, p, n);
    }

    public async Task<Result<SellerOrderDto>> GetAsync(Guid pageId, Guid id, bool hideClosedPhone, CancellationToken ct)
    {
        var o = await kit.Orders.FirstOrDefaultAsync(x => x.Id == id && x.PageId == pageId, ct);
        if (o is null) return StoreErrors.OrderNotFound;
        return ToSellerOrder(o, await Db.Users.FirstOrDefaultAsync(u => u.Id == o.CustomerUserId, ct), hideClosedPhone);
    }

    public async Task<Result<SellerOrderDto>> MoveAsync(Guid pageId, Guid actorId, Guid id, SetOrderStatusDto dto, bool hideClosedPhone, CancellationToken ct)
    {
        if (!EnumText.TryParse<StoreOrderStatus>(dto.Status, out var to)) return Errors.BadEnum("status");
        await using var tx = await Db.BeginTransactionAsync(ct);
        await Db.LockStoreOrderAsync(id, ct);
        var o = await kit.Orders.FirstOrDefaultAsync(x => x.Id == id && x.PageId == pageId, ct);
        if (o is null) return StoreErrors.OrderNotFound;
        if (!StoreOrderFlow.SellerCanMove(o.Status, to)) return StoreErrors.BadTransition;
        o.Record(to, s.Now);
        o.StatusReason = dto.Reason?.Trim();
        if (StoreOrderFlow.RestoresStock(to)) await kit.RestoreStockAsync(o, ct);
        Db.AuditLogs.Add(new AuditLog { ActorUserId = actorId, Action = "store_order.status", EntityName = nameof(StoreOrder),
            EntityId = o.Id.ToString(), Data = JsonSerializer.Serialize(new { to = to.Code() }) });
        await Db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await notifier.NotifyAsync(o.CustomerUserId, texts.Get("store.status.title", Shared.OrderNo(o.Number)),
            texts.Get($"store.status.{to.Code()}"), $"/store/orders/{o.Id}", ct);
        return ToSellerOrder(o, await Db.Users.FirstOrDefaultAsync(u => u.Id == o.CustomerUserId, ct), hideClosedPhone);
    }

    private SellerOrderDto ToSellerOrder(StoreOrder o, User? u, bool hideClosedPhone)
    {
        var showPhone = u is not null && !string.IsNullOrEmpty(u.PhoneEncrypted) && (!hideClosedPhone || IsActive(o.Status));
        return new(kit.ToDto(o), u?.Name, showPhone ? phones.Decrypt(u!.PhoneEncrypted) : null);
    }
}

/// <summary>The signed-in page's own store orders, for the page app. Phones are shown only while an order is still open.</summary>
public sealed class PageStoreOrderService(StoreOrderDesk desk, Shared s)
{
    public async Task<Result<PagedDto<SellerOrderDto>>> ListAsync(string? status, int? pageNo, int? size, CancellationToken ct)
    {
        var page = await s.MyPageAsync(ct);
        if (page is null) return Errors.NotPage;
        if (pageNo is < 1 || size is < 1) return Error.Validation("paging.invalid", "Page and page size start at 1");
        return await desk.ListAsync(page.Id, status, pageNo, size, allowGroups: true, hideClosedPhone: true, ct);
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
