using Dawwerli.Application.Abstractions;
using Dawwerli.Application.Contracts;
using Dawwerli.Domain.Common;
using Dawwerli.Domain.Entities;
using Dawwerli.Domain.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Dawwerli.Application.Services;

public sealed class OfferService(Shared s, WalletService wallet, TrustService trust, INotifier notifier, IRealtime realtime,
    ITextCatalog texts, IPhoneProtector phones, IOptions<RegionOptions> region)
{
    private IAppDbContext Db => s.Db;
    private readonly TimeZoneInfo _tz = TimeZoneInfo.FindSystemTimeZoneById(region.Value.TimeZone);

    private static readonly OrderStatus[] ActiveOrder = [OrderStatus.InPurchase, OrderStatus.AwaitingDelivery];

    public async Task<Result<OffersBoardDto>> BoardAsync(Guid requestId, CancellationToken ct)
    {
        var r = await Db.Requests.FirstOrDefaultAsync(x => x.Id == requestId, ct);
        if (r is null) return Error.NotFound("request");
        if (r.CustomerId != s.Me.UserId) return Errors.NotOwner;
        var rules = await s.Rules(ct);
        var offers = await Db.Offers.Include(o => o.Page).Where(o => o.RequestId == requestId && o.Status != OfferStatus.Withdrawn)
            .OrderByDescending(o => o.Sponsored).ThenBy(o => o.Price).ToListAsync(ct);
        return new OffersBoardDto(requestId, rules.MaxOffers, offers.Select(o => s.ToDto(o, o.Page!, rules)).ToList());
    }

    public async Task<Result<IdDto>> SubmitAsync(Guid requestId, SubmitOfferDto dto, CancellationToken ct)
    {
        if (!EnumText.TryParseItemCondition(dto.Condition, out var condition)) return Errors.BadEnum("condition");
        var pageRes = await s.ActivePageAsync(ct);
        if (!pageRes.IsSuccess) return pageRes.Error!;
        var page = pageRes.Value!;
        var rules = await s.Rules(ct);

        await using var tx = await Db.BeginTransactionAsync(ct);
        await Db.LockRequestAsync(requestId, ct);
        var r = await Db.Requests.FirstOrDefaultAsync(x => x.Id == requestId, ct);
        if (r is null) return Error.NotFound("request");
        await Db.Entry(r).ReloadAsync(ct);
        var now = s.Now;
        if (OfferPolicy.Check(r, dto.Price, dto.Note ?? "", page.HasEarlyAccess(now), now, rules) is { } err) return err;
        if (await Db.Offers.AnyAsync(o => o.RequestId == r.Id && o.PageId == page.Id, ct))
            return Error.Conflict("offer.duplicate", "You already sent an offer");
        if (await s.IsBlockedAsync(page.UserId, r.CustomerId, ct)) return Error.Forbidden("offer.blocked", "Blocked");
        await Db.Entry(page).ReloadAsync(ct);
        var fee = TrustPolicy.OfferFee(rules);
        if (page.Balance < fee)
            return Error.Payment("wallet.insufficient", $"رصيدك ما يكفي. كل عرض يخصم {fee} دينار");

        var early = OfferPolicy.IsEarlyOffer(r, now);
        var sponsored = r.SponsoredOfferId is null && await Db.AdCampaigns.AnyAsync(a => a.PageId == page.Id &&
            a.Slot == AdSlot.SponsoredOffer && a.Status == AdStatus.Active && a.StartAt <= now && a.EndAt > now, ct);
        var offer = new Offer
        {
            RequestId = r.Id, PageId = page.Id, Price = dto.Price, DeliveryHours = dto.DeliveryHours, Condition = condition,
            Note = dto.Note?.Trim() ?? "", PhotoKey = dto.PhotoKey, Status = OfferStatus.Active, Early = early, Sponsored = sponsored,
            PhotoStamp = $"{page.Name} · {TimeZoneInfo.ConvertTimeFromUtc(now, _tz):yyyy-MM-dd HH:mm}",
        };
        Db.Offers.Add(offer);
        if (sponsored) r.SponsoredOfferId = offer.Id;
        wallet.Post(page, LedgerType.SelectionFee, -fee, texts.Get("ledger.offerFee", r.Title), null, offer.Id);
        r.OffersCount++;
        if (early) r.EarlyOffersCount++;
        if (r.OffersCount >= rules.MaxOffers) r.Status = RequestStatus.Locked;
        await Db.RequestDispatches.Where(d => d.RequestId == r.Id && d.PageId == page.Id && d.SeenAt == null)
            .ExecuteUpdateAsync(x => x.SetProperty(d => d.SeenAt, now), ct);
        await Db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await notifier.NotifyAsync(r.CustomerId, texts.Get("customer.newOffer.title"), texts.Get("customer.newOffer.body", page.Name, dto.Price),
            $"/requests/{r.Id}/offers", ct);
        await realtime.RequestChangedAsync(r.Id, ct);
        return new IdDto(offer.Id);
    }

    public async Task<Result> WithdrawAsync(Guid offerId, CancellationToken ct)
    {
        var page = await s.MyPageAsync(ct);
        if (page is null) return Errors.NotPage;
        await using var tx = await Db.BeginTransactionAsync(ct);
        var o = await Db.Offers.FirstOrDefaultAsync(x => x.Id == offerId && x.PageId == page.Id, ct);
        if (o is null) return Error.NotFound("offer");
        await Db.LockRequestAsync(o.RequestId, ct);
        if (o.Status != OfferStatus.Active) return Error.Conflict("offer.state", "Offer cannot be withdrawn");
        var r = await Db.Requests.FirstAsync(x => x.Id == o.RequestId, ct);
        await Db.Entry(r).ReloadAsync(ct);
        o.Status = OfferStatus.Withdrawn;
        r.OffersCount--;
        if (o.Early) r.EarlyOffersCount--;
        if (r.SponsoredOfferId == o.Id) r.SponsoredOfferId = null;
        if (r.Status == RequestStatus.Locked) r.Status = r.WidenedTo.Length > 0 ? RequestStatus.Expanded : RequestStatus.Open;
        await Db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await realtime.RequestChangedAsync(r.Id, ct);
        return Result.Ok();
    }

    public async Task<Result> SetHiddenAsync(Guid offerId, bool hidden, CancellationToken ct)
    {
        var o = await Db.Offers.Include(x => x.Request).FirstOrDefaultAsync(x => x.Id == offerId, ct);
        if (o is null) return Error.NotFound("offer");
        if (o.Request!.CustomerId != s.Me.UserId) return Errors.NotOwner;
        if (!o.Request.IsLive && o.Request.Status != RequestStatus.Selected) return Error.Conflict("request.state", "Request is closed");
        o.Hidden = hidden;
        await Db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<Result<SelectionDto>> SelectAsync(Guid requestId, SelectDto dto, CancellationToken ct)
    {
        var rules = await s.Rules(ct);
        await using var tx = await Db.BeginTransactionAsync(ct);
        await Db.LockRequestAsync(requestId, ct);
        var r = await Db.Requests.FirstOrDefaultAsync(x => x.Id == requestId, ct);
        if (r is null) return Error.NotFound("request");
        await Db.Entry(r).ReloadAsync(ct);
        if (r.CustomerId != s.Me.UserId) return Errors.NotOwner;
        if (!r.IsLive) return Error.Conflict("request.state", "Request cannot be selected now");
        if (await Db.Orders.AnyAsync(o => o.RequestId == r.Id && ActiveOrder.Contains(o.Status), ct))
            return Error.Conflict("selection.exists", "An offer is already selected");
        var offer = await Db.Offers.Include(o => o.Page).FirstOrDefaultAsync(o => o.Id == dto.OfferId && o.RequestId == r.Id, ct);
        if (offer is null) return Error.NotFound("offer");
        if (offer.Status is not (OfferStatus.Active or OfferStatus.NotSelected)) return Error.Conflict("offer.state", "Offer unavailable");
        var page = offer.Page!;
        if (!page.IsActive(s.Now)) return Error.Conflict("page.unavailable", "Page is not available");

        var order = new Order
        {
            Number = await Db.NextNumberAsync("order_number", ct), RequestId = r.Id, OfferId = offer.Id, PageId = page.Id,
            CustomerId = r.CustomerId, Status = OrderStatus.InPurchase, SelectedAt = s.Now,
        };
        Db.Orders.Add(order);
        wallet.CaptureSelection(page, order, rules);
        await Db.Offers.Where(o => o.RequestId == r.Id && o.Id != offer.Id && o.Status == OfferStatus.Active)
            .ExecuteUpdateAsync(x => x.SetProperty(o => o.Status, OfferStatus.NotSelected), ct);
        offer.Status = OfferStatus.Selected;
        r.Status = RequestStatus.Selected;
        await Db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        await notifier.NotifyAsync(page.UserId, texts.Get("page.chosen.title"), texts.Get("page.chosen.body", Shared.OrderNo(order.Number)), "/seller/offers", ct);
        var losers = await Db.Offers.Where(o => o.RequestId == r.Id && o.Status == OfferStatus.NotSelected).Select(o => o.Page!.UserId).ToListAsync(ct);
        foreach (var u in losers) await notifier.NotifyAsync(u, texts.Get("page.notChosen.title"), texts.Get("page.notChosen.body", r.Title), "/seller/offers", ct);
        await realtime.RequestChangedAsync(r.Id, ct);
        return await ToSelectionAsync(order, ct);
    }

    public async Task<Result<SelectionDto?>> SelectionAsync(Guid requestId, CancellationToken ct)
    {
        var order = await Db.Orders.Where(o => o.RequestId == requestId && o.CustomerId == s.Me.UserId && o.Status != OrderStatus.NoReply)
            .OrderByDescending(o => o.SelectedAt).FirstOrDefaultAsync(ct);
        return order is null ? Result<SelectionDto?>.Ok(null) : await ToSelectionAsync(order, ct);
    }

    private async Task<SelectionDto> ToSelectionAsync(Order order, CancellationToken ct)
    {
        var rules = await s.Rules(ct);
        var offer = await Db.Offers.Include(o => o.Page).ThenInclude(p => p!.User).FirstAsync(o => o.Id == order.OfferId, ct);
        var r = await Db.Requests.FirstAsync(x => x.Id == order.RequestId, ct);
        var no = Shared.OrderNo(order.Number);
        string? phone = null, link = null;
        if (order.PhoneVisible && offer.Page!.User is { PhoneEncrypted: not "" } pu)
        {
            phone = phones.Decrypt(pu.PhoneEncrypted);
            var text = Uri.EscapeDataString(texts.Get("whatsapp.selection", no, r.Title, offer.Price));
            link = $"{region.Value.WhatsAppBaseUrl}{new string(phone.Where(char.IsDigit).ToArray())}?text={text}";
        }
        return new SelectionDto(r.Id, order.Id, no, r.Title, s.ToDto(offer, offer.Page!, rules), order.SelectedAt, phone,
            rules.ReplyWindowMin * 60, link);
    }

    public async Task<Result<NoReplyResultDto>> NoReplyAsync(Guid orderId, CancellationToken ct)
    {
        var rules = await s.Rules(ct);
        var order = await Db.Orders.Include(o => o.Page).FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null) return Error.NotFound("order");
        if (order.CustomerId != s.Me.UserId) return Errors.NotOwner;
        if (order.Status != OrderStatus.InPurchase) return Error.Conflict("order.state", "Order is not waiting for a reply");
        if (s.Now < order.SelectedAt.AddMinutes(rules.ReplyWindowMin)) return Error.Rule("order.too_early", "Wait for the reply window");

        if (order.ContactedAt is not null)
        {
            order.Status = OrderStatus.Disputed;
            await trust.OpenSystemDisputeAsync(order, DisputeType.NoReply, texts.Get("dispute.contactedButNoReply"), ct);
            await Db.SaveChangesAsync(ct);
            return new NoReplyResultDto(null);
        }

        await using var tx = await Db.BeginTransactionAsync(ct);
        await Db.LockRequestAsync(order.RequestId, ct);
        var r = await Db.Requests.FirstAsync(x => x.Id == order.RequestId, ct);
        order.Status = OrderStatus.NoReply;
        wallet.ReleaseSelection(order.Page!, order);
        await trust.AddPageMarkAsync(order.Page!, PageMarkKind.NoReply, order.Id, texts.Get("mark.noReply", Shared.OrderNo(order.Number)), rules, ct);
        await Db.Offers.Where(o => o.Id == order.OfferId).ExecuteUpdateAsync(x => x.SetProperty(o => o.Status, OfferStatus.Closed), ct);
        await Db.Offers.Where(o => o.RequestId == r.Id && o.Status == OfferStatus.NotSelected)
            .ExecuteUpdateAsync(x => x.SetProperty(o => o.Status, OfferStatus.Active), ct);
        r.Status = r.OffersCount >= rules.MaxOffers ? RequestStatus.Locked : RequestStatus.Open;
        await Db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var next = await Db.Offers.Include(o => o.Page).Where(o => o.RequestId == r.Id && o.Status == OfferStatus.Active && !o.Hidden)
            .OrderBy(o => o.Price).FirstOrDefaultAsync(ct);
        await realtime.RequestChangedAsync(r.Id, ct);
        return new NoReplyResultDto(next is null ? null : s.ToDto(next, next.Page!, rules));
    }
}
