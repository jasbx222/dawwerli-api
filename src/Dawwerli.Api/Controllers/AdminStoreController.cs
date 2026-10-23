using Dawwerli.Api.Infrastructure;
using Dawwerli.Application.Contracts;
using Dawwerli.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dawwerli.Api.Controllers;

[Route("api/admin/v1"), Authorize(Roles = Roles.Admin)]
public sealed class AdminStoreController(AdminStoreService store, CourierService couriers) : AppController
{
    [Authorize(Roles = AdminRoles.Super), HttpGet("seller-accounts")]
    public async Task<IActionResult> Accounts([FromQuery] Guid? pageId, CancellationToken ct) => Ok(await store.AccountsAsync(pageId, ct));
    [Authorize(Roles = AdminRoles.Super), HttpPost("seller-accounts")]
    public async Task<IActionResult> CreateAccount(CreateSellerAccountDto dto, CancellationToken ct) => From(await store.CreateAccountAsync(dto, ct));
    [Authorize(Roles = AdminRoles.Super), HttpPost("seller-accounts/{id:guid}/reset-password")]
    public async Task<IActionResult> ResetPassword(Guid id, ResetSellerPasswordDto dto, CancellationToken ct) => From(await store.ResetPasswordAsync(id, dto, ct));
    [Authorize(Roles = AdminRoles.Super), HttpPost("seller-accounts/{id:guid}/disable")]
    public async Task<IActionResult> Disable(Guid id, CancellationToken ct) => From(await store.SetDisabledAsync(id, true, ct));
    [Authorize(Roles = AdminRoles.Super), HttpPost("seller-accounts/{id:guid}/enable")]
    public async Task<IActionResult> Enable(Guid id, CancellationToken ct) => From(await store.SetDisabledAsync(id, false, ct));

    [Authorize(Roles = AdminRoles.Moderation), HttpGet("products")]
    public async Task<IActionResult> Products([FromQuery] Guid? pageId, [FromQuery] bool? hidden, [FromQuery] string? q, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        Ok(await store.ProductsAsync(pageId, hidden, q, page, pageSize, ct));
    [Authorize(Roles = AdminRoles.Moderation), HttpPost("products/{id:guid}/hide")]
    public async Task<IActionResult> Hide(Guid id, CancellationToken ct) => From(await store.SetHiddenAsync(id, true, ct));
    [Authorize(Roles = AdminRoles.Moderation), HttpPost("products/{id:guid}/unhide")]
    public async Task<IActionResult> Unhide(Guid id, CancellationToken ct) => From(await store.SetHiddenAsync(id, false, ct));

    [Authorize(Roles = AdminRoles.Moderation), HttpGet("reviews")]
    public async Task<IActionResult> Reviews([FromQuery] Guid? productId, [FromQuery] Guid? pageId, [FromQuery] bool? hidden, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        Ok(await store.ReviewsAsync(productId, pageId, hidden, page, pageSize, ct));
    [Authorize(Roles = AdminRoles.Moderation), HttpPost("reviews/{id:guid}/hide")]
    public async Task<IActionResult> HideReview(Guid id, CancellationToken ct) => From(await store.SetReviewHiddenAsync(id, true, ct));
    [Authorize(Roles = AdminRoles.Moderation), HttpPost("reviews/{id:guid}/unhide")]
    public async Task<IActionResult> UnhideReview(Guid id, CancellationToken ct) => From(await store.SetReviewHiddenAsync(id, false, ct));

    [HttpGet("pages/featured")] public async Task<IActionResult> Featured(CancellationToken ct) => Ok(await store.FeaturedPagesAsync(ct));
    [Authorize(Roles = AdminRoles.Super), HttpPut("pages/featured")]
    public async Task<IActionResult> SetFeatured(ReorderFeaturedDto dto, CancellationToken ct) => From(await store.SetFeaturedPagesAsync(dto, ct));

    [HttpGet("store/settings")] public async Task<IActionResult> Settings(CancellationToken ct) => Ok(await store.SettingsAsync(ct));
    [Authorize(Roles = AdminRoles.Super), HttpPut("store/settings")]
    public async Task<IActionResult> SaveSettings(StoreSettingsDto dto, CancellationToken ct) => From(await store.SaveSettingsAsync(dto, ct));
    [Authorize(Roles = AdminRoles.Super), HttpPut("pages/{id:guid}/product-limit")]
    public async Task<IActionResult> PageLimit(Guid id, PageProductLimitDto dto, CancellationToken ct) => From(await store.SetPageLimitAsync(id, dto, ct));
    /// <summary>Read-only list of a page's couriers (deleted ones excluded).</summary>
    [HttpGet("pages/{id:guid}/couriers")]
    public async Task<IActionResult> PageCouriers(Guid id, CancellationToken ct) => From(await couriers.AdminListAsync(id, ct));

    [HttpGet("store/orders")]
    public async Task<IActionResult> Orders([FromQuery] string? status, [FromQuery] Guid? pageId, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        From(await store.OrdersAsync(status, pageId, page, pageSize, ct));
}
