using Dawwerli.Application.Abstractions;
using Dawwerli.Application.Contracts;
using Dawwerli.Domain.Common;
using Dawwerli.Domain.Entities;
using Dawwerli.Domain.Rules;
using Microsoft.EntityFrameworkCore;

namespace Dawwerli.Application.Services;

/// <summary>camelCase wire names for enums ("notReceived" ⇄ NotReceived).</summary>
public static class EnumText
{
    public static string Code<T>(this T value) where T : struct, Enum
    {
        var s = value.ToString();
        return char.ToLowerInvariant(s[0]) + s[1..];
    }

    public static bool TryParse<T>(string? text, out T value) where T : struct, Enum =>
        Enum.TryParse(text?.Replace("_", ""), ignoreCase: true, out value) && Enum.IsDefined(value);

    /// <summary>Accepts API codes (new, used, any) and the Arabic labels the customer app sends.</summary>
    public static bool TryParseItemCondition(string? text, out ItemCondition value)
    {
        var normalized = text?.Trim().Replace(" ", "").Replace("_", "").Replace("-", "");
        normalized = normalized switch
        {
            "جديد" or "جديدة" => "new",
            "مستعمل" or "مستخدم" or "مستخدمة" => "used",
            "أي" or "اي" or "الكل" or "أيحالة" or "ايحالة" => "any",
            _ => normalized
        };
        return TryParse(normalized, out value);
    }
}

public static class Errors
{
    public static Error BadEnum(string field) => Error.Validation($"{field}.invalid", $"Unknown {field}");
    public static readonly Error NotPage = Error.Forbidden("page.required", "A page account is required");
    public static readonly Error PageNotVerified = Error.Forbidden("page.not_verified", "Page is not verified");
    public static readonly Error PageSuspended = Error.Forbidden("page.suspended", "Page is suspended");
    public static readonly Error NotOwner = Error.Forbidden("forbidden", "Not yours");
}

/// <summary>Shared lookups/mapping used by several services.</summary>
public sealed class Shared(IAppDbContext db, ICurrentUser me, IStorage storage, IClock clock, IRulesProvider rules)
{
    public IAppDbContext Db => db;
    public ICurrentUser Me => me;
    public IClock Clock => clock;
    public DateTime Now => clock.UtcNow;
    public Task<BusinessRules> Rules(CancellationToken ct) => rules.GetAsync(ct);
    public string? Url(string? key) => storage.Url(key);

    public Task<Page?> MyPageAsync(CancellationToken ct) =>
        db.Pages.Include(p => p.Categories).Include(p => p.Governorates).FirstOrDefaultAsync(p => p.UserId == me.UserId, ct);

    /// <summary>Current page that is verified and not suspended.</summary>
    public async Task<Result<Page>> ActivePageAsync(CancellationToken ct)
    {
        var page = await MyPageAsync(ct);
        if (page is null) return Errors.NotPage;
        if (page.Status != PageStatus.Verified) return Errors.PageNotVerified;
        if (page.SuspendedUntil > Now) return Errors.PageSuspended;
        return page;
    }

    public OfferDto ToDto(Offer o, Page p, BusinessRules r) => new(o.Id, p.Id, p.Name, Url(o.PhotoKey), o.Price,
        Math.Round(p.Rating, 1), p.Deals, p.Deals >= r.TrustedDeals, o.DeliveryHours, o.Condition.Code(), o.Note, o.PhotoStamp,
        o.Sponsored, o.Hidden, o.CreatedAt);

    public static string OrderNo(long n) => $"DW-{n}";

    public async Task<bool> IsBlockedAsync(Guid a, Guid b, CancellationToken ct) =>
        await db.Blocks.AnyAsync(x => (x.BlockerUserId == a && x.BlockedUserId == b) || (x.BlockerUserId == b && x.BlockedUserId == a), ct);

    public void Audit(string action, string entity, object id, string data = "{}") =>
        db.AuditLogs.Add(new AuditLog { ActorUserId = me.UserId, Action = action, EntityName = entity, EntityId = id.ToString()!, Data = data });
}
