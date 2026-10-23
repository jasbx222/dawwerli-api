using System.Security.Claims;
using System.Text.Json;
using Dawwerli.Application.Abstractions;
using Dawwerli.Domain.Common;
using Dawwerli.Domain.Entities;
using Dawwerli.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace Dawwerli.Api.Infrastructure;

public sealed class HttpCurrentUser(IHttpContextAccessor http, SellerActingPage acting) : ICurrentUser
{
    private ClaimsPrincipal? P => http.HttpContext?.User;
    public Guid UserId => acting.PageUserId ?? (Guid.TryParse(P?.FindFirstValue(ClaimTypes.NameIdentifier) ?? P?.FindFirstValue("sub"), out var id) ? id : Guid.Empty);
    public UserRole Role => acting.PageUserId is not null ? UserRole.Page : Enum.TryParse<UserRole>(P?.FindFirstValue(ClaimTypes.Role), out var r) ? r : UserRole.Customer;
    public string? DeviceKey => http.HttpContext?.Request.Headers["X-Device-Id"].FirstOrDefault();
}

/// <summary>
/// Set only by <see cref="ActAsSellerPageAttribute"/> on seller portal page routes: the page owner the signed-in
/// seller account acts for, so the existing page services run unchanged and stay scoped to that one page.
/// </summary>
public sealed class SellerActingPage { public Guid? PageUserId { get; set; } }

/// <summary>Validates the seller account (enabled, password stamp) and acts as its page for this request.
/// Runs before <see cref="IdempotentAttribute"/> so idempotency keys are scoped to the page.</summary>
public sealed class ActAsSellerPageAttribute : Attribute, IAsyncActionFilter, IOrderedFilter
{
    public int Order => -100;

    public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
    {
        var sp = ctx.HttpContext.RequestServices;
        var owner = await sp.GetRequiredService<Dawwerli.Application.Services.SellerPortalService>().PageOwnerAsync(ctx.HttpContext.RequestAborted);
        if (!owner.IsSuccess) { ctx.Result = ProblemMapping.ToProblem(owner.Error!); return; }
        sp.GetRequiredService<SellerActingPage>().PageUserId = owner.Value;
        await next();
    }
}

/// <summary>Web seller portal token (role + scope + page id, see SellerTokenService).</summary>
public sealed class HttpCurrentSeller(IHttpContextAccessor http) : ICurrentSeller
{
    private ClaimsPrincipal? P => http.HttpContext?.User;
    public Guid AccountId => Guid.TryParse(P?.FindFirstValue(ClaimTypes.NameIdentifier) ?? P?.FindFirstValue("sub"), out var id) ? id : Guid.Empty;
    public Guid PageId => Guid.TryParse(P?.FindFirstValue(Dawwerli.Infrastructure.Services.AppClaims.PageId), out var id) ? id : Guid.Empty;
    public string? Stamp => P?.FindFirstValue(Dawwerli.Infrastructure.Services.SellerPortalClaims.Stamp);
}

public static class Roles
{
    public const string SellerPortal = Dawwerli.Infrastructure.Services.SellerPortalClaims.Role;
    /// <summary>Policy: SellerPortal role AND seller_portal scope.</summary>
    public const string SellerPortalPolicy = "SellerPortal";
    public const string Customer = nameof(UserRole.Customer);
    public const string Page = nameof(UserRole.Page);
    public const string Admin = nameof(UserRole.Admin);
}

/// <summary>Back-office permission sets; combined with the Admin role on every admin endpoint.</summary>
public static class AdminRoles
{
    public const string Super = nameof(AdminRole.SuperAdmin);
    public const string Moderation = nameof(AdminRole.SuperAdmin) + "," + nameof(AdminRole.Moderator);
    public const string Finance = nameof(AdminRole.SuperAdmin) + "," + nameof(AdminRole.Finance);

    public static string? Of(ClaimsPrincipal user) =>
        Enum.GetNames<AdminRole>().FirstOrDefault(user.IsInRole);
}

/// <summary>Maps <see cref="Result"/> errors to RFC 7807 ProblemDetails with a stable "code".</summary>
public static class ProblemMapping
{
    public static int Status(ErrorKind k) => k switch
    {
        ErrorKind.Validation => StatusCodes.Status400BadRequest,
        ErrorKind.Unauthorized => StatusCodes.Status401Unauthorized,
        ErrorKind.Forbidden => StatusCodes.Status403Forbidden,
        ErrorKind.NotFound => StatusCodes.Status404NotFound,
        ErrorKind.Conflict => StatusCodes.Status409Conflict,
        ErrorKind.PaymentRequired => StatusCodes.Status402PaymentRequired,
        ErrorKind.RateLimited => StatusCodes.Status429TooManyRequests,
        _ => StatusCodes.Status422UnprocessableEntity,
    };

    public static ObjectResult ToProblem(Error e)
    {
        var pd = new ProblemDetails { Status = Status(e.Kind), Title = e.Message, Type = $"https://dawwerli.app/errors/{e.Code}" };
        pd.Extensions["code"] = e.Code;
        if (e.Data is not null) foreach (var (k, v) in e.Data) pd.Extensions[k] = v;
        return new ObjectResult(pd) { StatusCode = pd.Status, ContentTypes = { "application/problem+json" } };
    }
}

[ApiController]
[Produces("application/json")]
public abstract class AppController : ControllerBase
{
    protected IActionResult From(Result r) => r.IsSuccess ? NoContent() : ProblemMapping.ToProblem(r.Error!);
    protected IActionResult From<T>(Result<T> r) => r.IsSuccess ? (r.Value is null ? NoContent() : Ok(r.Value)) : ProblemMapping.ToProblem(r.Error!);
}

/// <summary>Runs FluentValidation on every action argument that has a validator (→ 400).</summary>
public sealed class ValidationFilter(IServiceProvider sp) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
    {
        foreach (var arg in ctx.ActionArguments.Values.Where(a => a is not null))
        {
            if (sp.GetService(typeof(IValidator<>).MakeGenericType(arg!.GetType())) is not IValidator v) continue;
            var res = await v.ValidateAsync(new ValidationContext<object>(arg), ctx.HttpContext.RequestAborted);
            if (res.IsValid) continue;
            var errors = res.Errors.GroupBy(e => JsonNamingPolicy.CamelCase.ConvertName(e.PropertyName))
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
            var pd = new ValidationProblemDetails(errors) { Status = 400, Title = "Validation failed" };
            pd.Extensions["code"] = "validation";
            ctx.Result = new BadRequestObjectResult(pd);
            return;
        }
        await next();
    }
}

/// <summary>Same Idempotency-Key from the same user returns the stored response (POST only).</summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotentAttribute : Attribute, IAsyncActionFilter
{
    public const string Header = "Idempotency-Key";

    public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
    {
        var key = ctx.HttpContext.Request.Headers[Header].FirstOrDefault();
        var user = ctx.HttpContext.RequestServices.GetRequiredService<ICurrentUser>().UserId;
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100 || user == Guid.Empty) { await next(); return; }
        var db = ctx.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var hit = await db.IdempotencyRecords.AsNoTracking().FirstOrDefaultAsync(r => r.UserId == user && r.Key == key);
        if (hit is not null)
        {
            ctx.Result = new ContentResult { StatusCode = hit.StatusCode, Content = hit.Body, ContentType = "application/json" };
            return;
        }
        var done = await next();
        if (done.Result is ObjectResult { StatusCode: null or (>= 200 and < 300) } ok)
        {
            db.ChangeTracker.Clear();
            db.IdempotencyRecords.Add(new IdempotencyRecord
            {
                UserId = user, Key = key, StatusCode = ok.StatusCode ?? 200, CreatedAt = DateTime.UtcNow,
                Body = JsonSerializer.Serialize(ok.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            });
            try { await db.SaveChangesAsync(); } catch (DbUpdateException) { /* concurrent duplicate: first one wins */ }
        }
    }
}

/// <summary>Returns 426 when X-App-Version is below the configured minimum.</summary>
public sealed class AppVersionMiddleware(RequestDelegate next)
{
    public const string Header = "X-App-Version";

    public async Task InvokeAsync(HttpContext ctx, IRulesProvider rules)
    {
        var sent = ctx.Request.Headers[Header].FirstOrDefault();
        if (sent is not null && Version.TryParse(sent, out var v) && Version.TryParse((await rules.GetAsync(ctx.RequestAborted)).MinAppVersion, out var min) && v < min)
        {
            ctx.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
            await ctx.Response.WriteAsJsonAsync(new ProblemDetails { Status = 426, Title = "Update required", Extensions = { ["code"] = "app.update_required", ["minVersion"] = min.ToString() } });
            return;
        }
        await next(ctx);
    }
}
