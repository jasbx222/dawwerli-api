using System.Text.Json;
using Dawwerli.Application.Abstractions;
using Dawwerli.Application.Contracts;
using Dawwerli.Domain.Common;
using Dawwerli.Domain.Entities;
using Dawwerli.Domain.Rules;
using Microsoft.EntityFrameworkCore;

namespace Dawwerli.Application.Services;

public sealed class RequestService(Shared s, DispatchService dispatch, AdsService ads, IRealtime realtime)
{
    private IAppDbContext Db => s.Db;

    public static class Codes
    {
        public const string Incomplete = "incomplete", SizeMissing = "sizeMissing", Prohibited = "prohibited", ContactInfo = "contactInfo",
            MaxOpen = "maxOpen", VoiceTooLong = "voiceTooLong", PriceZero = "priceZero", NotReady = "notReady", Banned = "postingBanned";
    }

    private Task<int> OpenCountAsync(CancellationToken ct) => Db.Requests.CountAsync(r => r.CustomerId == s.Me.UserId &&
        (r.Status == RequestStatus.Open || r.Status == RequestStatus.Expanded || r.Status == RequestStatus.Locked), ct);

    public Task<int> OpenRequestsCountAsync(CancellationToken ct) => OpenCountAsync(ct);

    public async Task<Result<IdDto>> CreateAsync(CreateRequestDto dto, CancellationToken ct)
    {
        if (!EnumText.TryParse<InputMode>(dto.Mode, out var mode)) return Errors.BadEnum("mode");
        if (!EnumText.TryParseItemCondition(dto.Condition, out var condition)) return Errors.BadEnum("condition");
        var rules = await s.Rules(ct);
        var user = await Db.Users.FirstAsync(u => u.Id == s.Me.UserId, ct);
        if (user.IsBanned || user.PostingBannedUntil > s.Now)
            return new Error(ErrorKind.Forbidden, Codes.Banned, "Posting is banned", new Dictionary<string, object?> { ["until"] = user.PostingBannedUntil });
        var category = await Db.Categories.FirstOrDefaultAsync(c => c.Id == dto.CategoryId && c.IsActive, ct);
        if (category is null) return Error.NotFound("category");
        if (!await Db.Governorates.AnyAsync(g => g.Id == dto.GovernorateId, ct)) return Error.NotFound("governorate");
        Page? target = null;
        if (dto.PageId is { } pageId)
        {
            target = await Db.Pages.FirstOrDefaultAsync(p => p.Id == pageId, ct);
            if (target is null) return Error.NotFound("page");
        }

        var text = dto.Description?.Trim() ?? "";
        var banned = await Db.BannedKeywords.Select(b => b.Word).ToListAsync(ct);
        var schema = CatalogService.ParseSchema(category.FieldSchema);
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        var errors = new List<string>();
        if (dto.MediaKey is null && dto.VoiceSeconds is null && words < rules.MinWordsWithoutMedia) errors.Add(Codes.Incomplete);
        if (ContactFilter.HasContact(text)) errors.Add(Codes.ContactInfo);
        if (ContactFilter.FindBanned(text, banned) is not null) errors.Add(Codes.Prohibited);
        if (await OpenCountAsync(ct) >= rules.MaxOpenRequests) errors.Add(Codes.MaxOpen);
        if (dto.VoiceSeconds > rules.MaxVoiceSeconds) errors.Add(Codes.VoiceTooLong);
        if (dto.MaxPrice <= 0) errors.Add(Codes.PriceZero);
        if (schema.Required.Contains("size") && string.IsNullOrWhiteSpace(dto.Size)) errors.Add(Codes.SizeMissing);
        if (!dto.ReadyToBuy) errors.Add(Codes.NotReady);
        if (errors.Count > 0)
            return new Error(ErrorKind.BusinessRule, "request.invalid", "Request rejected", new Dictionary<string, object?> { ["errors"] = errors });

        var r = new Request
        {
            CustomerId = user.Id, Number = await Db.NextNumberAsync("request_number", ct), Description = text, Mode = mode,
            MediaKey = dto.MediaKey, VoiceSeconds = dto.VoiceSeconds, CategoryId = category.Id, Condition = condition,
            MaxPrice = dto.MaxPrice, GovernorateId = dto.GovernorateId, Status = RequestStatus.Open,
            Fields = JsonSerializer.Serialize(new { size = dto.Size, color = dto.Color, pageId = dto.PageId }),
            ExpandsAt = s.Now.AddMinutes(rules.ExpandAfterMin), ExpiresAt = s.Now.AddHours(rules.ExpireAfterHours),
        };
        if (string.IsNullOrEmpty(r.Description)) r.Description = category.Name;
        Db.Requests.Add(r);
        await Db.SaveChangesAsync(ct);
        if (target is null) await dispatch.PublishAsync(r, rules, ct);
        else await dispatch.PublishToPageAsync(r, target, ct);
        await Db.SaveChangesAsync(ct);
        return new IdDto(r.Id);
    }

    public async Task<IReadOnlyList<RequestSummaryDto>> MineAsync(CancellationToken ct)
    {
        var rules = await s.Rules(ct);
        var rows = await Db.Requests.Where(r => r.CustomerId == s.Me.UserId).OrderByDescending(r => r.CreatedAt).Take(100)
            .Select(r => new { r, OrderId = Db.Orders.Where(o => o.RequestId == r.Id && o.Status != OrderStatus.NoReply).Select(o => (Guid?)o.Id).FirstOrDefault() })
            .ToListAsync(ct);
        return rows.Select(x => new RequestSummaryDto(x.r.Id, x.r.Number, x.r.Title, s.Url(x.r.MediaKey), x.r.Status.Code(), x.r.OffersCount,
            rules.MaxOffers, x.r.CreatedAt, x.OrderId)).ToList();
    }

    private async Task<Result<Request>> OwnAsync(Guid id, CancellationToken ct)
    {
        var r = await Db.Requests.Include(x => x.Governorate).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return Error.NotFound("request");
        if (r.CustomerId != s.Me.UserId) return Errors.NotOwner;
        return r;
    }

    public async Task<Result<LiveRequestDto>> LiveAsync(Guid id, CancellationToken ct)
    {
        var res = await OwnAsync(id, ct);
        if (!res.IsSuccess) return res.Error!;
        return await ToLiveAsync(res.Value!, ct);
    }

    public async Task<LiveRequestDto> ToLiveAsync(Request r, CancellationToken ct)
    {
        var rules = await s.Rules(ct);
        var reached = await Db.RequestDispatches.CountAsync(d => d.RequestId == r.Id, ct);
        var seen = await Db.RequestDispatches.CountAsync(d => d.RequestId == r.Id && d.SeenAt != null, ct);
        var prices = await Db.Offers.Where(o => o.RequestId == r.Id && o.Status != OfferStatus.Withdrawn)
            .OrderBy(o => o.CreatedAt).Select(o => o.Price).ToListAsync(ct);
        var widened = await Db.Governorates.Where(g => r.WidenedTo.Contains(g.Id)).Select(g => g.Name).ToListAsync(ct);
        long? suggested = null;
        if (r.WidenedTo.Length > 0 && prices.Count == 0)
        {
            var cat = await Db.Categories.FirstAsync(c => c.Id == r.CategoryId, ct);
            suggested = CatalogService.ParseSchema(cat.FieldSchema).PriceSuggestions.Where(p => p > r.MaxPrice).Cast<long?>().FirstOrDefault();
        }
        var orderId = await Db.Orders.Where(o => o.RequestId == r.Id && o.Status != OrderStatus.NoReply).Select(o => (Guid?)o.Id).FirstOrDefaultAsync(ct);
        var gov = r.Governorate?.Name ?? await Db.Governorates.Where(g => g.Id == r.GovernorateId).Select(g => g.Name).FirstAsync(ct);
        return new LiveRequestDto(r.Id, r.Title, s.Url(r.MediaKey), r.MaxPrice, reached, seen, prices, rules.MaxOffers, r.ExpandsAt, r.ExpiresAt,
            r.Status.Code(), widened, (int)(s.Now - r.CreatedAt).TotalMinutes, suggested,
            await ads.BannerAsync(r.GovernorateId, r.CategoryId, ct), gov, orderId);
    }

    public async Task<Result> CancelAsync(Guid id, CancellationToken ct)
    {
        var res = await OwnAsync(id, ct);
        if (!res.IsSuccess) return res.Error!;
        var r = res.Value!;
        if (!r.IsLive) return Error.Conflict("request.state", "Request cannot be cancelled now");
        r.Status = RequestStatus.Cancelled;
        await Db.Offers.Where(o => o.RequestId == r.Id && o.Status == OfferStatus.Active)
            .ExecuteUpdateAsync(x => x.SetProperty(o => o.Status, OfferStatus.Closed), ct);
        await Db.SaveChangesAsync(ct);
        await realtime.RequestChangedAsync(r.Id, ct);
        return Result.Ok();
    }

    public async Task<Result> RaisePriceAsync(Guid id, RaisePriceDto dto, CancellationToken ct)
    {
        var res = await OwnAsync(id, ct);
        if (!res.IsSuccess) return res.Error!;
        var r = res.Value!;
        if (!r.AcceptsOffers) return Error.Conflict("request.state", "Request is not open");
        if (dto.MaxPrice <= r.MaxPrice) return Error.Validation("maxPrice.lower", "New price must be higher");
        r.MaxPrice = dto.MaxPrice;
        await Db.SaveChangesAsync(ct);
        await realtime.RequestChangedAsync(r.Id, ct);
        return Result.Ok();
    }

    public async Task<Result<IdDto>> RepublishAsync(Guid id, CancellationToken ct)
    {
        var res = await OwnAsync(id, ct);
        if (!res.IsSuccess) return res.Error!;
        var r = res.Value!;
        if (r.IsLive) return Error.Conflict("request.state", "Request is still live");
        var fields = JsonSerializer.Deserialize<Dictionary<string, string?>>(r.Fields) ?? [];
        Guid? pageId = Guid.TryParse(fields.GetValueOrDefault("pageId"), out var parsed) ? parsed : null;
        return await CreateAsync(new CreateRequestDto(r.Mode.Code(), r.MediaKey, r.Description, r.VoiceSeconds, r.CategoryId,
            fields.GetValueOrDefault("size"), fields.GetValueOrDefault("color"), r.Condition.Code(), r.MaxPrice, r.GovernorateId, true, pageId), ct);
    }
}
