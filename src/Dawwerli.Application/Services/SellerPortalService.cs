using System.Text.Json;
using Dawwerli.Application.Abstractions;
using Dawwerli.Application.Contracts;
using Dawwerli.Domain.Common;
using Dawwerli.Domain.Entities;
using Dawwerli.Domain.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Dawwerli.Application.Services;

/// <summary>Web seller portal: email + password sign-in (lockout, no 2FA) and the page's own products, orders and stats.</summary>
public sealed class SellerPortalService(StoreKit kit, Shared s, ICurrentSeller seller, IPasswordHasher passwords, ISellerTokenService tokens,
    IOptions<SellerPortalOptions> portal, INotifier notifier, ITextCatalog texts, IPhoneProtector phones)
{
    private IAppDbContext Db => kit.Db;
    private static readonly Error BadCredentials = Error.Unauthorized("auth.credentials", "Wrong email or password");
    /// <summary>Verified against when the email is unknown, so both cases cost one hash and take the same time.</summary>
    private static string? _dummyHash;

    public async Task<Result<SellerSessionDto>> LoginAsync(SellerLoginDto dto, CancellationToken ct)
    {
        var email = Normalize(dto.Email);
        var now = s.Now;
        var acc = await Db.SellerAccounts.Include(a => a.Page).FirstOrDefaultAsync(a => a.Email == email, ct);
        // Unknown emails lock the same way known ones do, so a 429 never tells whether an account exists.
        var lockedUntil = acc is null ? UnknownEmailLocks.LockedUntil(email, now) : acc.LockedUntil;
        if (lockedUntil > now)
            return Error.Limited("auth.locked", "Too many wrong passwords", (int)Math.Ceiling((lockedUntil.Value - now).TotalSeconds));
        var ok = passwords.Verify(dto.Password ?? "", acc?.PasswordHash ?? (_dummyHash ??= passwords.Hash(Guid.NewGuid().ToString())));
        if (acc is null || !ok)
        {
            if (acc is null) UnknownEmailLocks.Fail(email, now, portal.Value.MaxFailedLogins, portal.Value.LockMinutes);
            else
            {
                if (++acc.FailedLogins >= portal.Value.MaxFailedLogins)
                {
                    acc.FailedLogins = 0;
                    acc.LockedUntil = now.AddMinutes(portal.Value.LockMinutes);
                }
                await Db.SaveChangesAsync(ct);
            }
            return BadCredentials;
        }
        if (acc.Disabled || acc.Page is null) return Error.Forbidden("seller.disabled", "Seller account is disabled");
        acc.FailedLogins = 0;
        acc.LockedUntil = null;
        acc.LastLoginAt = now;
        await Db.SaveChangesAsync(ct);
        return new SellerSessionDto(tokens.Create(acc), tokens.AccessTokenSeconds, acc.PageId, acc.Page.Name);
    }

    public static string Normalize(string? email) => (email ?? "").Trim().ToLowerInvariant();

    /// <summary>Every portal call re-checks the account so a disabled account stops working before its token expires.</summary>
    private async Task<Result<(SellerAccount Account, Page Page)>> ContextAsync(CancellationToken ct)
    {
        var acc = await Db.SellerAccounts.Include(a => a.Page).FirstOrDefaultAsync(a => a.Id == seller.AccountId && a.PageId == seller.PageId, ct);
        if (acc is null || acc.Disabled || acc.Page is null) return Error.Unauthorized("seller.disabled", "Seller account is disabled");
        if (seller.Stamp != SellerAccount.StampOf(acc.PasswordHash)) return Error.Unauthorized("seller.session_expired", "Password changed, sign in again");
        return (acc, acc.Page);
    }

    public async Task<Result<SellerMeDto>> MeAsync(CancellationToken ct)
    {
        var c = await ContextAsync(ct);
        if (!c.IsSuccess) return c.Error!;
        var (acc, page) = c.Value!;
        return new SellerMeDto(acc.Id, acc.Email, page.Id, page.Name, page.Status.Code(),
            await kit.ProductCountAsync(page.Id, ct), await kit.ProductLimitAsync(page, ct));
    }

    // ---------- products ----------

    public async Task<Result<PagedDto<SellerProductDto>>> ProductsAsync(string? q, int? pageNo, int? size, CancellationToken ct)
    {
        var c = await ContextAsync(ct);
        if (!c.IsSuccess) return c.Error!;
        var (p, n) = kit.Paging(pageNo, size);
        var query = Db.Products.Where(x => x.PageId == seller.PageId && x.DeletedAt == null);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => x.Name.ToLower().Contains(q.Trim().ToLower()));
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.CreatedAt).Skip((p - 1) * n).Take(n).ToListAsync(ct);
        return new PagedDto<SellerProductDto>(rows.Select(kit.ToSellerDto).ToList(), total, p, n);
    }

    public async Task<Result<SellerProductDto>> ProductAsync(Guid id, CancellationToken ct)
    {
        var c = await ContextAsync(ct);
        if (!c.IsSuccess) return c.Error!;
        var p = await OwnProductAsync(id, ct);
        return p is null ? StoreErrors.ProductNotFound : kit.ToSellerDto(p);
    }

    public async Task<Result<SellerProductDto>> CreateProductAsync(UpsertProductDto dto, CancellationToken ct)
    {
        var c = await ContextAsync(ct);
        if (!c.IsSuccess) return c.Error!;
        var page = c.Value.Page;
        if (CanSell(page) is { } closed) return closed;
        if (await CheckAsync(dto, ct) is { } bad) return bad;
        // The page row lock makes concurrent creates count one after another, so the limit can't be overshot.
        await using var tx = await Db.BeginTransactionAsync(ct);
        await Db.LockPageAsync(page.Id, ct);
        var limit = await kit.ProductLimitAsync(page, ct);
        if (await kit.ProductCountAsync(page.Id, ct) >= limit) return StoreErrors.LimitReached(limit);
        var p = new Product { PageId = page.Id };
        Apply(p, dto);
        Db.Products.Add(p);
        await Db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return kit.ToSellerDto(p);
    }

    public async Task<Result<SellerProductDto>> UpdateProductAsync(Guid id, UpsertProductDto dto, CancellationToken ct)
    {
        var c = await ContextAsync(ct);
        if (!c.IsSuccess) return c.Error!;
        if (CanSell(c.Value.Page) is { } closed) return closed;
        var p = await OwnProductAsync(id, ct);
        if (p is null) return StoreErrors.ProductNotFound;
        if (await CheckAsync(dto, ct) is { } bad) return bad;
        Apply(p, dto);
        await Db.SaveChangesAsync(ct);
        return kit.ToSellerDto(p);
    }

    /// <summary>Soft delete: past orders keep pointing at the product.</summary>
    public async Task<Result> DeleteProductAsync(Guid id, CancellationToken ct)
    {
        var c = await ContextAsync(ct);
        if (!c.IsSuccess) return c.Error!;
        var p = await OwnProductAsync(id, ct);
        if (p is null) return StoreErrors.ProductNotFound;
        p.DeletedAt = s.Now;
        await Db.CartItems.Where(i => i.ProductId == id).ExecuteDeleteAsync(ct);
        await Db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    /// <summary>Product changes need a verified page that isn't suspended; orders already placed can still be handled.</summary>
    private Error? CanSell(Page page) =>
        page.Status != PageStatus.Verified ? Errors.PageNotVerified
        : page.SuspendedUntil > s.Now ? Errors.PageSuspended
        : null;

    private Task<Product?> OwnProductAsync(Guid id, CancellationToken ct) =>
        Db.Products.FirstOrDefaultAsync(x => x.Id == id && x.PageId == seller.PageId && x.DeletedAt == null, ct);

    private async Task<Error?> CheckAsync(UpsertProductDto dto, CancellationToken ct)
    {
        var o = kit.Options;
        if (dto.ImageKeys.Length > o.MaxImagesPerProduct)
            return new Error(ErrorKind.Validation, "product.images", "Too many images", new Dictionary<string, object?> { ["max"] = o.MaxImagesPerProduct });
        if (dto.ImageKeys.Any(k => !k.StartsWith(StoreOptions.ProductImageFolder + "/", StringComparison.Ordinal) || k.Contains("..")))
            return Error.Validation("product.image_key", "Upload images with kind=products first");
        if (!await Db.Categories.AnyAsync(x => x.Id == dto.CategoryId && x.IsActive, ct)) return Errors.BadEnum("category");
        if (await kit.BannedWordAsync(ct, dto.Name, dto.Description) is { } word)
            return new Error(ErrorKind.BusinessRule, "product.prohibited", "Text contains a banned word", new Dictionary<string, object?> { ["word"] = word });
        return null;
    }

    private static void Apply(Product p, UpsertProductDto dto)
    {
        p.Name = dto.Name.Trim();
        p.Description = dto.Description?.Trim() ?? "";
        p.Price = dto.Price;
        p.ImageKeys = dto.ImageKeys;
        p.CategoryId = dto.CategoryId;
        p.Stock = dto.Stock;
        p.Active = dto.Active;
    }

    // ---------- orders ----------

    public async Task<Result<PagedDto<SellerOrderDto>>> OrdersAsync(string? status, int? pageNo, int? size, CancellationToken ct)
    {
        var c = await ContextAsync(ct);
        if (!c.IsSuccess) return c.Error!;
        var (p, n) = kit.Paging(pageNo, size);
        var q = kit.Orders.Where(o => o.PageId == seller.PageId);
        if (status is not null)
        {
            if (!EnumText.TryParse<StoreOrderStatus>(status, out var st)) return Errors.BadEnum("status");
            q = q.Where(o => o.Status == st);
        }
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(o => o.CreatedAt).Skip((p - 1) * n).Take(n).ToListAsync(ct);
        var ids = rows.Select(r => r.CustomerUserId).Distinct().ToList();
        var users = await Db.Users.Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, ct);
        return new PagedDto<SellerOrderDto>(rows.Select(o => ToSellerOrder(o, users.GetValueOrDefault(o.CustomerUserId))).ToList(), total, p, n);
    }

    public async Task<Result<SellerOrderDto>> OrderAsync(Guid id, CancellationToken ct)
    {
        var c = await ContextAsync(ct);
        if (!c.IsSuccess) return c.Error!;
        var o = await kit.Orders.FirstOrDefaultAsync(x => x.Id == id && x.PageId == seller.PageId, ct);
        if (o is null) return StoreErrors.OrderNotFound;
        return ToSellerOrder(o, await Db.Users.FirstOrDefaultAsync(u => u.Id == o.CustomerUserId, ct));
    }

    public async Task<Result<SellerOrderDto>> SetStatusAsync(Guid id, SetOrderStatusDto dto, CancellationToken ct)
    {
        var c = await ContextAsync(ct);
        if (!c.IsSuccess) return c.Error!;
        if (!EnumText.TryParse<StoreOrderStatus>(dto.Status, out var to)) return Errors.BadEnum("status");
        await using var tx = await Db.BeginTransactionAsync(ct);
        await Db.LockStoreOrderAsync(id, ct);
        var o = await kit.Orders.FirstOrDefaultAsync(x => x.Id == id && x.PageId == seller.PageId, ct);
        if (o is null) return StoreErrors.OrderNotFound;
        if (!StoreOrderFlow.SellerCanMove(o.Status, to)) return StoreErrors.BadTransition;
        o.Record(to, s.Now);
        o.StatusReason = dto.Reason?.Trim();
        if (StoreOrderFlow.RestoresStock(to)) await kit.RestoreStockAsync(o, ct);
        AuditStatus(o, to);
        await Db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await notifier.NotifyAsync(o.CustomerUserId, texts.Get("store.status.title", Shared.OrderNo(o.Number)),
            texts.Get($"store.status.{to.Code()}"), $"/store/orders/{o.Id}", ct);
        return ToSellerOrder(o, await Db.Users.FirstOrDefaultAsync(u => u.Id == o.CustomerUserId, ct));
    }

    private void AuditStatus(StoreOrder o, StoreOrderStatus to) =>
        Db.AuditLogs.Add(new AuditLog { ActorUserId = seller.AccountId, Action = "store_order.status", EntityName = nameof(StoreOrder),
            EntityId = o.Id.ToString(), Data = JsonSerializer.Serialize(new { to = to.Code() }) });

    private SellerOrderDto ToSellerOrder(StoreOrder o, User? u) =>
        new(kit.ToDto(o), u?.Name, u is null || string.IsNullOrEmpty(u.PhoneEncrypted) ? null : phones.Decrypt(u.PhoneEncrypted));

    // ---------- stats ----------

    public async Task<Result<SellerStatsDto>> StatsAsync(CancellationToken ct)
    {
        var c = await ContextAsync(ct);
        if (!c.IsSuccess) return c.Error!;
        var pageId = seller.PageId;
        var byStatus = await Db.StoreOrders.Where(o => o.PageId == pageId).GroupBy(o => o.Status)
            .Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var sales = await Db.StoreOrders.Where(o => o.PageId == pageId && o.Status == StoreOrderStatus.Delivered).SumAsync(o => (long?)o.Subtotal, ct) ?? 0;
        var top = await Db.StoreOrderItems
            .Where(i => Db.StoreOrders.Any(o => o.Id == i.OrderId && o.PageId == pageId && o.Status == StoreOrderStatus.Delivered))
            .GroupBy(i => new { i.ProductId, i.Name })
            .Select(g => new { g.Key.ProductId, g.Key.Name, Qty = g.Sum(i => i.Quantity), Sales = g.Sum(i => i.UnitPrice * i.Quantity) })
            .OrderByDescending(x => x.Qty).Take(kit.Options.TopProductsCount).ToListAsync(ct);
        var active = await Db.Products.CountAsync(p => p.PageId == pageId && p.DeletedAt == null && p.Active && !p.HiddenByAdmin, ct);
        return new SellerStatsDto(byStatus.Sum(x => x.Count), byStatus.ToDictionary(x => x.Key.Code(), x => x.Count), sales, active,
            top.Select(t => new TopProductDto(t.ProductId, t.Name, t.Qty, t.Sales)).ToList());
    }
}

/// <summary>Failed-login counters for emails with no seller account (kept in memory, bounded).</summary>
internal static class UnknownEmailLocks
{
    private const int MaxEntries = 10_000;
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (int Fails, DateTime? Until)> Entries = new();

    public static DateTime? LockedUntil(string email, DateTime now) =>
        Entries.TryGetValue(email, out var e) && e.Until > now ? e.Until : null;

    public static void Fail(string email, DateTime now, int maxFails, int lockMinutes)
    {
        if (Entries.Count >= MaxEntries)
            foreach (var k in Entries.Where(x => x.Value.Until is null || x.Value.Until <= now).Select(x => x.Key).ToList()) Entries.TryRemove(k, out _);
        Entries.AddOrUpdate(email, (1, null), (_, e) =>
        {
            if (e.Until is { } u && u <= now) e = (0, null);
            var fails = e.Fails + 1;
            return fails >= maxFails ? (0, now.AddMinutes(lockMinutes)) : (fails, e.Until);
        });
    }
}
