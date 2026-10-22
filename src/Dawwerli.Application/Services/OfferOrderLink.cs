using Dawwerli.Application.Abstractions;
using Dawwerli.Application.Contracts;
using Dawwerli.Domain.Common;
using Dawwerli.Domain.Entities;
using Dawwerli.Domain.Rules;
using Microsoft.EntityFrameworkCore;

namespace Dawwerli.Application.Services;

/// <summary>Checks the optional delivery pin and the free-text notes that travel with an order.</summary>
public static class OrderPin
{
    public static readonly Error ContactError = Error.Rule("order.contact", ContactFilter.ArabicMessage);

    public static Error? Check(double? lat, double? lng, params string?[] notes)
    {
        if (lat.HasValue != lng.HasValue) return Error.Validation("location.incomplete", "حدد الموقع كامل على الخريطة أو اتركه فارغ");
        if (lat is { } a && (!double.IsFinite(a) || a is < -90 or > 90)) return Error.Validation("location.range", "الموقع على الخريطة مو صحيح");
        if (lng is { } b && (!double.IsFinite(b) || b is < -180 or > 180)) return Error.Validation("location.range", "الموقع على الخريطة مو صحيح");
        return notes.Any(ContactFilter.HasContact) ? ContactError : null;
    }

    public static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

/// <summary>
/// Keeps an accepted offer and its store order in step. Choosing an offer opens a store order (New) that the page moves
/// through the same lifecycle as a cart order; the offer-side <see cref="Order"/> keeps inspect-before-pay, mismatch,
/// disputes and the rating.
/// </summary>
public sealed class OfferOrderLink(Shared s, WalletService wallet, INotifier notifier, ITextCatalog texts)
{
    private IAppDbContext Db => s.Db;

    public static readonly Error Disputed = Error.Conflict("order.disputed", "الطلب عليه نزاع أو انحسم، ما تكدر تغيّر حالته");

    /// <summary>Opens the store order for a chosen offer (caller saves, inside its transaction).</summary>
    public StoreOrder Open(Order order, Offer offer, Request r, SelectDto dto)
    {
        var now = s.Now;
        var so = new StoreOrder
        {
            Number = order.Number, Kind = StoreOrderKind.Offer, RequestId = r.Id, OfferOrderId = order.Id, CustomerUserId = order.CustomerId,
            PageId = order.PageId, Status = StoreOrderStatus.New, PaymentMethod = PaymentMethod.Cash, PaymentStatus = PaymentStatus.CashOnDelivery,
            Address = OrderPin.Clean(dto.Address) ?? "", GovernorateId = r.GovernorateId,
            Latitude = dto.Latitude, Longitude = dto.Longitude, LocationNote = OrderPin.Clean(dto.LocationNote),
            StatusChangedAt = now, Subtotal = offer.Price,
            Items = [new StoreOrderItem { ProductId = Guid.Empty, Name = r.Title, UnitPrice = offer.Price, Quantity = 1, ImageKey = offer.PhotoKey }],
        };
        so.History.Add(new StoreOrderEvent { OrderId = so.Id, Status = StoreOrderStatus.New, At = now });
        Db.StoreOrders.Add(so);
        return so;
    }

    public Task<StoreOrder?> ForDealAsync(Guid orderId, CancellationToken ct) =>
        Db.StoreOrders.Include(o => o.History).FirstOrDefaultAsync(o => o.OfferOrderId == orderId, ct);

    private static bool IsOpen(Order deal) => deal.Status is OrderStatus.InPurchase or OrderStatus.AwaitingDelivery;

    /// <summary>Applies a page status move to the offer side. Returns an error when the deal is already decided or disputed.</summary>
    public async Task<Error?> OnPageMoveAsync(StoreOrder so, StoreOrderStatus to, CancellationToken ct)
    {
        var deal = await Db.Orders.FirstOrDefaultAsync(o => o.Id == so.OfferOrderId, ct);
        if (deal is null) return null;
        // Once the customer confirmed receipt, only "delivered" is left (and the store order is already there).
        if (!IsOpen(deal)) return Disputed;
        var now = s.Now;
        switch (to)
        {
            case StoreOrderStatus.Accepted:
                deal.ContactedAt ??= now;
                deal.Status = OrderStatus.AwaitingDelivery;
                break;
            case StoreOrderStatus.OutForDelivery:
                deal.ContactedAt ??= now;
                deal.Status = OrderStatus.AwaitingDelivery;
                break;
            case StoreOrderStatus.Delivered:
                deal.DeliveryAskedAt = now;
                break;
            case StoreOrderStatus.Rejected or StoreOrderStatus.Cancelled:
                await CloseAndReopenAsync(deal, refund: false, ct);
                break;
        }
        return null;
    }

    /// <summary>The customer cancels a chosen offer before the page accepted it: the fee goes back and the request reopens.</summary>
    public async Task<Error?> OnCustomerCancelAsync(StoreOrder so, CancellationToken ct)
    {
        var deal = await Db.Orders.Include(o => o.Page).FirstOrDefaultAsync(o => o.Id == so.OfferOrderId, ct);
        if (deal is null) return null;
        if (deal.Status != OrderStatus.InPurchase) return Disputed;
        await CloseAndReopenAsync(deal, refund: true, ct);
        return null;
    }

    private async Task CloseAndReopenAsync(Order deal, bool refund, CancellationToken ct)
    {
        var rules = await s.Rules(ct);
        await Db.LockRequestAsync(deal.RequestId, ct);
        var r = await Db.Requests.FirstAsync(x => x.Id == deal.RequestId, ct);
        await Db.Entry(r).ReloadAsync(ct);
        deal.Status = OrderStatus.Closed;
        if (refund)
        {
            var page = deal.Page ?? await Db.Pages.FirstAsync(p => p.Id == deal.PageId, ct);
            wallet.ReleaseSelection(page, deal);
        }
        await Db.Offers.Where(o => o.Id == deal.OfferId).ExecuteUpdateAsync(x => x.SetProperty(o => o.Status, OfferStatus.Closed), ct);
        await Db.Offers.Where(o => o.RequestId == r.Id && o.Status == OfferStatus.NotSelected)
            .ExecuteUpdateAsync(x => x.SetProperty(o => o.Status, OfferStatus.Active), ct);
        r.Status = r.ExpiresAt <= s.Now ? RequestStatus.Expired : r.OffersCount >= rules.MaxOffers ? RequestStatus.Locked : RequestStatus.Open;
    }

    public Task NotifyReopenedAsync(StoreOrder so, CancellationToken ct) =>
        so.RequestId is null ? Task.CompletedTask : notifier.NotifyAsync(so.CustomerUserId, texts.Get("store.dealReopened.title"),
            texts.Get("store.dealReopened.body", Shared.OrderNo(so.Number)), $"/requests/{so.RequestId}/offers", ct);
}

/// <summary>The signed-in page's couriers. Name and phone are copied onto an order when it goes out for delivery.</summary>
public sealed class CourierService(Shared s)
{
    private IAppDbContext Db => s.Db;

    private static CourierDto ToDto(Courier c) => new(c.Id, c.Name, c.Phone, c.Active);

    /// <summary>Iraqi mobile in local form (07XXXXXXXXX); accepts +964 / 964 / Arabic digits.</summary>
    public static string? NormalizePhone(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var d = new string(ContactFilter.Normalize(raw).Where(char.IsAsciiDigit).ToArray());
        if (d.StartsWith("00964")) d = "0" + d[5..];
        else if (d.StartsWith("964")) d = "0" + d[3..];
        else if (d.Length == 10 && d.StartsWith('7')) d = "0" + d;
        return d.Length == 11 && d.StartsWith("07") ? d : null;
    }

    private static readonly Error BadPhone = Error.Validation("courier.phone", "رقم المندوب لازم يكون رقم عراقي مثل 07701234567");

    public async Task<Result<IReadOnlyList<CourierDto>>> ListAsync(CancellationToken ct) =>
        await s.MyPageAsync(ct) is { } page ? await ListForPageAsync(page.Id, ct) : Errors.NotPage;

    public async Task<Result<CourierDto>> CreateAsync(UpsertCourierDto dto, CancellationToken ct) =>
        await s.MyPageAsync(ct) is { } page ? await CreateForPageAsync(page.Id, dto, ct) : Errors.NotPage;

    public async Task<Result<CourierDto>> UpdateAsync(Guid id, UpsertCourierDto dto, CancellationToken ct) =>
        await s.MyPageAsync(ct) is { } page ? await UpdateForPageAsync(page.Id, id, dto, ct) : Errors.NotPage;

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct) =>
        await s.MyPageAsync(ct) is { } page ? await DeleteForPageAsync(page.Id, id, ct) : Errors.NotPage;

    /// <summary>Admin view of any page's couriers (read only).</summary>
    public async Task<Result<IReadOnlyList<CourierDto>>> AdminListAsync(Guid pageId, CancellationToken ct) =>
        await Db.Pages.AnyAsync(p => p.Id == pageId, ct) ? await ListForPageAsync(pageId, ct) : Error.NotFound("page");

    // Page-scoped core, shared by the app (pages/me) and the web seller portal.

    public async Task<Result<IReadOnlyList<CourierDto>>> ListForPageAsync(Guid pageId, CancellationToken ct)
    {
        var rows = await Db.Couriers.Where(c => c.PageId == pageId && c.DeletedAt == null).OrderByDescending(c => c.Active).ThenBy(c => c.Name).ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<Result<CourierDto>> CreateForPageAsync(Guid pageId, UpsertCourierDto dto, CancellationToken ct)
    {
        if (NormalizePhone(dto.Phone) is not { } phone) return BadPhone;
        if (await Db.Couriers.CountAsync(c => c.PageId == pageId && c.DeletedAt == null, ct) >= 50)
            return Error.Rule("courier.limit", "وصلت الحد الأعلى للمناديب");
        var c = new Courier { PageId = pageId, Name = dto.Name.Trim(), Phone = phone, Active = dto.Active };
        Db.Couriers.Add(c);
        await Db.SaveChangesAsync(ct);
        return ToDto(c);
    }

    public async Task<Result<CourierDto>> UpdateForPageAsync(Guid pageId, Guid id, UpsertCourierDto dto, CancellationToken ct)
    {
        var c = await Db.Couriers.FirstOrDefaultAsync(x => x.Id == id && x.PageId == pageId && x.DeletedAt == null, ct);
        if (c is null) return Error.NotFound("courier");
        if (NormalizePhone(dto.Phone) is not { } phone) return BadPhone;
        c.Name = dto.Name.Trim();
        c.Phone = phone;
        c.Active = dto.Active;
        await Db.SaveChangesAsync(ct);
        return ToDto(c);
    }

    /// <summary>Soft delete: orders that already went out keep the courier's snapshot.</summary>
    public async Task<Result> DeleteForPageAsync(Guid pageId, Guid id, CancellationToken ct)
    {
        var c = await Db.Couriers.FirstOrDefaultAsync(x => x.Id == id && x.PageId == pageId && x.DeletedAt == null, ct);
        if (c is null) return Error.NotFound("courier");
        c.DeletedAt = s.Now;
        c.Active = false;
        await Db.SaveChangesAsync(ct);
        return Result.Ok();
    }
}
