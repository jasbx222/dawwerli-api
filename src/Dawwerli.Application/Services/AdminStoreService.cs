using System.Text.Json;
using Dawwerli.Application.Abstractions;
using Dawwerli.Application.Contracts;
using Dawwerli.Domain.Common;
using Dawwerli.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Dawwerli.Application.Services;

/// <summary>Back office for the page store: seller portal accounts, product moderation, limits and store orders. Every change is audited.</summary>
public sealed class AdminStoreService(StoreKit kit, Shared s, IPasswordHasher passwords, IRulesProvider rulesProvider, IOptions<SellerPortalOptions> portal)
{
    private IAppDbContext Db => kit.Db;

    // ---------- seller portal accounts ----------

    public async Task<IReadOnlyList<SellerAccountDto>> AccountsAsync(Guid? pageId, CancellationToken ct) =>
        await Db.SellerAccounts.Where(a => pageId == null || a.PageId == pageId).OrderByDescending(a => a.CreatedAt)
            .Select(a => new SellerAccountDto(a.Id, a.PageId, a.Page!.Name, a.Email, a.Disabled, a.LockedUntil, a.LastLoginAt, a.CreatedAt)).ToListAsync(ct);

    public async Task<Result<SellerAccountDto>> CreateAccountAsync(CreateSellerAccountDto dto, CancellationToken ct)
    {
        if (PasswordError(dto.Password) is { } bad) return bad;
        var page = await Db.Pages.FirstOrDefaultAsync(p => p.Id == dto.PageId, ct);
        if (page is null) return Error.NotFound("page");
        var email = SellerPortalService.Normalize(dto.Email);
        if (await Db.SellerAccounts.AnyAsync(a => a.Email == email, ct)) return Error.Conflict("seller.email_taken", "Email already used");
        var acc = new SellerAccount { PageId = page.Id, Email = email, PasswordHash = passwords.Hash(dto.Password) };
        Db.SellerAccounts.Add(acc);
        s.Audit("seller_account.create", nameof(SellerAccount), acc.Id, JsonSerializer.Serialize(new { page.Id, email }));
        await Db.SaveChangesAsync(ct);
        return new SellerAccountDto(acc.Id, page.Id, page.Name, acc.Email, false, null, null, acc.CreatedAt);
    }

    public async Task<Result> ResetPasswordAsync(Guid id, ResetSellerPasswordDto dto, CancellationToken ct)
    {
        if (PasswordError(dto.Password) is { } bad) return bad;
        var acc = await Db.SellerAccounts.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (acc is null) return Error.NotFound("seller_account");
        acc.PasswordHash = passwords.Hash(dto.Password);
        acc.FailedLogins = 0;
        acc.LockedUntil = null;
        s.Audit("seller_account.reset_password", nameof(SellerAccount), id);
        await Db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<Result> SetDisabledAsync(Guid id, bool disabled, CancellationToken ct)
    {
        var acc = await Db.SellerAccounts.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (acc is null) return Error.NotFound("seller_account");
        acc.Disabled = disabled;
        s.Audit(disabled ? "seller_account.disable" : "seller_account.enable", nameof(SellerAccount), id);
        await Db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    private Error? PasswordError(string? password) =>
        string.IsNullOrEmpty(password) || password.Length < portal.Value.MinPasswordLength
            ? new Error(ErrorKind.Validation, "password.too_short", "Password is too short",
                new Dictionary<string, object?> { ["minLength"] = portal.Value.MinPasswordLength })
            : null;

    // ---------- products ----------

    public async Task<PagedDto<AdminProductDto>> ProductsAsync(Guid? pageId, bool? hidden, string? q, int? pageNo, int? size, CancellationToken ct)
    {
        var (p, n) = kit.Paging(pageNo, size);
        var query = Db.Products.AsQueryable();
        if (pageId is { } pid) query = query.Where(x => x.PageId == pid);
        if (hidden is { } h) query = query.Where(x => x.HiddenByAdmin == h);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => x.Name.ToLower().Contains(q.Trim().ToLower()));
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.CreatedAt).Skip((p - 1) * n).Take(n)
            .Select(x => new AdminProductDto(x.Id, x.PageId, x.Page!.Name, x.Name, x.Price, x.Stock, x.Active, x.HiddenByAdmin, x.DeletedAt != null, x.CreatedAt))
            .ToListAsync(ct);
        return new PagedDto<AdminProductDto>(rows, total, p, n);
    }

    public async Task<Result> SetHiddenAsync(Guid id, bool hidden, CancellationToken ct)
    {
        var p = await Db.Products.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p is null) return StoreErrors.ProductNotFound;
        p.HiddenByAdmin = hidden;
        if (hidden) await Db.CartItems.Where(i => i.ProductId == id).ExecuteDeleteAsync(ct);
        s.Audit(hidden ? "product.hide" : "product.unhide", nameof(Product), id);
        await Db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    // ---------- product reviews ----------

    public async Task<PagedDto<AdminReviewDto>> ReviewsAsync(Guid? productId, Guid? pageId, bool? hidden, int? pageNo, int? size, CancellationToken ct)
    {
        var (p, n) = kit.Paging(pageNo, size);
        var q = Db.ProductRatings.AsQueryable();
        if (productId is { } pid) q = q.Where(r => r.ProductId == pid);
        if (pageId is { } pg) q = q.Where(r => r.Product!.PageId == pg);
        if (hidden is { } h) q = q.Where(r => r.HiddenByAdmin == h);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(r => r.UpdatedAt).Skip((p - 1) * n).Take(n)
            .Select(r => new AdminReviewDto(r.Id, r.ProductId, r.Product!.Name, r.Product.PageId, r.Product.Page!.Name, r.User!.Name ?? "",
                r.Stars, r.Comment, r.HiddenByAdmin, r.UpdatedAt)).ToListAsync(ct);
        return new PagedDto<AdminReviewDto>(rows, total, p, n);
    }

    /// <summary>Hidden reviews leave the public list and the product's average.</summary>
    public async Task<Result> SetReviewHiddenAsync(Guid id, bool hidden, CancellationToken ct)
    {
        var r = await Db.ProductRatings.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return Error.NotFound("review");
        r.HiddenByAdmin = hidden;
        s.Audit(hidden ? "review.hide" : "review.unhide", nameof(ProductRating), id);
        await Db.SaveChangesAsync(ct);
        await Db.RefreshProductRatingAsync(r.ProductId, ct);
        return Result.Ok();
    }

    // ---------- featured pages (customer home strip) ----------

    public async Task<IReadOnlyList<AdminFeaturedPageDto>> FeaturedPagesAsync(CancellationToken ct) =>
        await Db.Pages.Where(p => p.FeaturedOrder != null).OrderBy(p => p.FeaturedOrder)
            .Select(p => new AdminFeaturedPageDto(p.Id, p.Name, s.Url(p.ImageKey), p.FeaturedOrder!.Value, p.Rating,
                Db.PageFollows.Count(f => f.PageId == p.Id))).ToListAsync(ct);

    /// <summary>Replaces the featured strip with these pages in this order (at most five; an empty list clears it).</summary>
    public async Task<Result> SetFeaturedPagesAsync(ReorderFeaturedDto dto, CancellationToken ct)
    {
        var ids = (dto.PageIds ?? []).Distinct().ToList();
        if (ids.Count > DiscoveryService.TopPagesMax)
            return new Error(ErrorKind.Validation, "featured.too_many", "Too many featured pages", new Dictionary<string, object?> { ["max"] = DiscoveryService.TopPagesMax });
        var pages = await Db.Pages.Where(p => ids.Contains(p.Id)).ToListAsync(ct);
        if (pages.Count != ids.Count) return Error.NotFound("page");
        if (pages.Any(p => p.Status != PageStatus.Verified)) return Error.Rule("featured.not_verified", "Only verified pages can be featured");
        await using var tx = await Db.BeginTransactionAsync(ct);
        await Db.Pages.Where(p => p.FeaturedOrder != null).ExecuteUpdateAsync(x => x.SetProperty(p => p.FeaturedOrder, (int?)null), ct);
        for (var i = 0; i < ids.Count; i++) pages.First(p => p.Id == ids[i]).FeaturedOrder = i + 1;
        s.Audit("pages.featured", nameof(Page), Guid.Empty, JsonSerializer.Serialize(new { ids }));
        await Db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Result.Ok();
    }

    // ---------- limits & commission ----------

    public async Task<StoreSettingsDto> SettingsAsync(CancellationToken ct)
    {
        var r = await rulesProvider.GetAsync(ct);
        return new StoreSettingsDto(r.MaxProductsPerPage > 0 ? r.MaxProductsPerPage : kit.Options.DefaultMaxProductsPerPage, r.StoreCommissionPercent);
    }

    public async Task<Result> SaveSettingsAsync(StoreSettingsDto dto, CancellationToken ct)
    {
        if (dto.MaxProductsPerPage < 1) return Error.Validation("maxProductsPerPage.invalid", "Must be at least 1");
        if (dto.CommissionPercent is < 0 or > 100) return Error.Validation("commissionPercent.invalid", "Must be 0–100");
        var r = await rulesProvider.GetAsync(ct);
        r.MaxProductsPerPage = dto.MaxProductsPerPage;
        r.StoreCommissionPercent = dto.CommissionPercent;
        await rulesProvider.UpdateAsync(r, ct);
        s.Audit("settings.store", "Settings", 1, JsonSerializer.Serialize(dto));
        await Db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<Result> SetPageLimitAsync(Guid pageId, PageProductLimitDto dto, CancellationToken ct)
    {
        if (dto.Limit is < 0) return Error.Validation("limit.invalid", "Must be 0 or more");
        var page = await Db.Pages.FirstOrDefaultAsync(p => p.Id == pageId, ct);
        if (page is null) return Error.NotFound("page");
        page.MaxProducts = dto.Limit;
        s.Audit("page.product_limit", nameof(Page), pageId, JsonSerializer.Serialize(dto));
        await Db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    // ---------- store orders ----------

    public async Task<Result<PagedDto<StoreOrderDto>>> OrdersAsync(string? status, Guid? pageId, int? pageNo, int? size, CancellationToken ct)
    {
        var (p, n) = kit.Paging(pageNo, size);
        var q = kit.Orders;
        if (pageId is { } pid) q = q.Where(o => o.PageId == pid);
        if (status is not null)
        {
            if (!EnumText.TryParse<StoreOrderStatus>(status, out var st)) return Errors.BadEnum("status");
            q = q.Where(o => o.Status == st);
        }
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(o => o.CreatedAt).Skip((p - 1) * n).Take(n).ToListAsync(ct);
        return new PagedDto<StoreOrderDto>(rows.Select(kit.ToDto).ToList(), total, p, n);
    }
}
