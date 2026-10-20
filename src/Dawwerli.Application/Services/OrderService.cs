using Dawwerli.Application.Abstractions;
using Dawwerli.Application.Contracts;
using Dawwerli.Domain.Common;
using Dawwerli.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dawwerli.Application.Services;

public sealed class OrderService(Shared s, TrustService trust, INotifier notifier, ITextCatalog texts)
{
    private IAppDbContext Db => s.Db;

    private async Task<Result<Order>> CustomerOrderAsync(Guid id, CancellationToken ct)
    {
        var o = await Db.Orders.Include(x => x.Page).Include(x => x.Offer).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (o is null) return Error.NotFound("order");
        return o.CustomerId == s.Me.UserId ? o : Errors.NotOwner;
    }

    private async Task<Result<Order>> PageOrderAsync(Guid id, CancellationToken ct)
    {
        var page = await s.MyPageAsync(ct);
        if (page is null) return Errors.NotPage;
        var o = await Db.Orders.Include(x => x.Page).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (o is null) return Error.NotFound("order");
        return o.PageId == page.Id ? o : Errors.NotOwner;
    }

    public async Task<Result<OrderDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var res = await CustomerOrderAsync(id, ct);
        if (!res.IsSuccess) return res.Error!;
        var o = res.Value!;
        var rules = await s.Rules(ct);
        var rating = await Db.Ratings.FirstOrDefaultAsync(r => r.OrderId == o.Id, ct);
        var received = o.Answer == DeliveryAnswer.Received;
        var steps = new List<OrderStepDto>
        {
            new(texts.Get("step.selected"), texts.Get("step.selected.detail", o.Page!.Name), true),
            new(texts.Get("step.contacted"), "", o.ContactedAt is not null),
            new(texts.Get("step.delivered"), "", received),
            new(texts.Get("step.rated"), "", rating is not null),
        };
        return new OrderDto(o.Id, Shared.OrderNo(o.Number), o.PageId, o.Page.Name, s.Url(o.Page.ImageKey), steps, received,
            o.Answer?.Code(), rating is null ? null : new RatingDto(rating.Stars, rating.Tags, rating.Comment), o.Status.Code(),
            rules.RatingTags, rules.LowRatingTags);
    }

    public async Task<Result> AnswerAsync(Guid id, DeliveryAnswerDto dto, CancellationToken ct)
    {
        if (!EnumText.TryParse<DeliveryAnswer>(dto.Answer, out var answer)) return Errors.BadEnum("answer");
        var res = await CustomerOrderAsync(id, ct);
        if (!res.IsSuccess) return res.Error!;
        var o = res.Value!;
        if (o.Status is not (OrderStatus.InPurchase or OrderStatus.AwaitingDelivery)) return Error.Conflict("order.state", "Already answered");
        o.Answer = answer;
        o.AnsweredAt = s.Now;
        o.Status = answer switch
        {
            DeliveryAnswer.Received => OrderStatus.Delivered,
            DeliveryAnswer.NotReceived => OrderStatus.NotDelivered,
            _ => OrderStatus.Mismatch,
        };
        if (answer == DeliveryAnswer.Received)
        {
            o.Page!.Deals++;
            await Db.Requests.Where(r => r.Id == o.RequestId).ExecuteUpdateAsync(x => x.SetProperty(r => r.Status, RequestStatus.Closed), ct);
            // A pending ghost report is disproved by the customer's confirmation.
            await Db.Reports.Where(r => r.OrderId == o.Id && r.Reason == ReportReason.NoReply && r.Status == CaseStatus.Open)
                .ExecuteUpdateAsync(x => x.SetProperty(r => r.Status, CaseStatus.Rejected), ct);
        }
        else await trust.OpenSystemDisputeAsync(o, DisputeType.NotDelivered, texts.Get($"dispute.{answer.Code()}"), ct);
        await Db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<Result> RateAsync(Guid id, RatingDto dto, CancellationToken ct)
    {
        var res = await CustomerOrderAsync(id, ct);
        if (!res.IsSuccess) return res.Error!;
        var o = res.Value!;
        if (o.Status != OrderStatus.Delivered)
            return o.Status == OrderStatus.Rated ? Error.Conflict("rating.exists", "Already rated") : Error.Conflict("order.state", "Confirm receipt first");
        if (dto.Stars is < 1 or > 5) return Error.Validation("stars.range", "Stars must be 1..5");
        var page = o.Page!;
        Db.Ratings.Add(new Rating { OrderId = o.Id, PageId = page.Id, Stars = dto.Stars, Tags = dto.Tags.ToArray(), Comment = dto.Comment?.Trim() ?? "" });
        page.Rating = (page.Rating * page.RatingCount + dto.Stars) / (page.RatingCount + 1);
        page.RatingCount++;
        o.Status = OrderStatus.Rated;
        await Db.SaveChangesAsync(ct);
        await notifier.NotifyAsync(page.UserId, texts.Get("page.rated.title"), texts.Get("page.rated.body", dto.Stars), "/seller/rating", ct);
        return Result.Ok();
    }

    public async Task<Result> ContactedAsync(Guid id, CancellationToken ct)
    {
        var res = await PageOrderAsync(id, ct);
        if (!res.IsSuccess) return res.Error!;
        var o = res.Value!;
        if (o.Status != OrderStatus.InPurchase) return Error.Conflict("order.state", "Order is not in purchase");
        o.ContactedAt = s.Now;
        o.Status = OrderStatus.AwaitingDelivery;
        await Db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<Result> ReportGhostAsync(Guid id, CancellationToken ct)
    {
        var res = await PageOrderAsync(id, ct);
        if (!res.IsSuccess) return res.Error!;
        var o = res.Value!;
        var rules = await s.Rules(ct);
        if (o.Status is not (OrderStatus.InPurchase or OrderStatus.AwaitingDelivery)) return Error.Conflict("order.state", "Order is closed");
        if (o.GhostReportedAt is not null) return Error.Conflict("ghost.exists", "Already reported");
        if (s.Now < o.SelectedAt.AddHours(rules.GhostAfterHours)) return Error.Rule("ghost.too_early", "Report allowed after the waiting window");
        o.GhostReportedAt = s.Now;
        Db.Reports.Add(new Report { ReporterUserId = s.Me.UserId, Target = ReportTarget.Customer, TargetId = o.CustomerId, OrderId = o.Id,
            Reason = ReportReason.NoReply, Status = CaseStatus.Open });
        await Db.SaveChangesAsync(ct);
        await notifier.NotifyAsync(o.CustomerId, texts.Get("customer.askReceived.title"), texts.Get("customer.askReceived.body", o.Page!.Name),
            $"/orders/{o.Id}", ct);
        return Result.Ok();
    }

    public async Task<Result<IdDto>> ReportCustomerAsync(Guid id, ReportDto dto, CancellationToken ct)
    {
        var res = await PageOrderAsync(id, ct);
        if (!res.IsSuccess) return res.Error!;
        return await trust.ReportAsync(dto with { Target = "customer", TargetId = res.Value!.CustomerId, OrderId = id }, ct);
    }
}
