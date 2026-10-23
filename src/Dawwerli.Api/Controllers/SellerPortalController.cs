using Dawwerli.Api.Infrastructure;
using Dawwerli.Application.Contracts;
using Dawwerli.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Dawwerli.Api.Controllers;

/// <summary>Web seller portal (email + password accounts created by admins). Image upload: POST api/seller/v1/media kind=products.</summary>
[Route("api/seller/v1"), Authorize(Policy = Roles.SellerPortalPolicy)]
public sealed class SellerPortalController(SellerPortalService portal) : AppController
{
    [AllowAnonymous, HttpPost("auth/login"), EnableRateLimiting(RateLimitPolicies.AdminLogin)]
    public async Task<IActionResult> Login(SellerLoginDto dto, CancellationToken ct) => From(await portal.LoginAsync(dto, ct));

    [HttpGet("me")] public async Task<IActionResult> Me(CancellationToken ct) => From(await portal.MeAsync(ct));

    [HttpGet("products")]
    public async Task<IActionResult> Products([FromQuery] string? q, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        From(await portal.ProductsAsync(q, page, pageSize, ct));

    [HttpGet("products/{id:guid}")] public async Task<IActionResult> Product(Guid id, CancellationToken ct) => From(await portal.ProductAsync(id, ct));
    [HttpPost("products")] public async Task<IActionResult> Create(UpsertProductDto dto, CancellationToken ct) => From(await portal.CreateProductAsync(dto, ct));
    [HttpPut("products/{id:guid}")] public async Task<IActionResult> Update(Guid id, UpsertProductDto dto, CancellationToken ct) => From(await portal.UpdateProductAsync(id, dto, ct));
    [HttpDelete("products/{id:guid}")] public async Task<IActionResult> Delete(Guid id, CancellationToken ct) => From(await portal.DeleteProductAsync(id, ct));

    [HttpGet("orders")]
    public async Task<IActionResult> Orders([FromQuery] string? status, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        From(await portal.OrdersAsync(status, page, pageSize, ct));

    [HttpGet("orders/{id:guid}")] public async Task<IActionResult> Order(Guid id, CancellationToken ct) => From(await portal.OrderAsync(id, ct));
    [HttpPost("orders/{id:guid}/status")] public async Task<IActionResult> Status(Guid id, SetOrderStatusDto dto, CancellationToken ct) => From(await portal.SetStatusAsync(id, dto, ct));

    [HttpGet("orders/{id:guid}/courier-location")] public async Task<IActionResult> CourierLocation(Guid id, CancellationToken ct) => From(await portal.CourierLocationAsync(id, ct));
    [HttpPost("orders/{id:guid}/tracking-link")] public async Task<IActionResult> RegenerateTrackingLink(Guid id, CancellationToken ct) => From(await portal.RegenerateLinkAsync(id, ct));
    [HttpPost("orders/{id:guid}/courier")] public async Task<IActionResult> ReassignCourier(Guid id, AssignCourierDto dto, CancellationToken ct) => From(await portal.ReassignCourierAsync(id, dto, ct));

    /// <summary>The page's couriers; pass courierId when moving an order to outForDelivery.</summary>
    [HttpGet("couriers")] public async Task<IActionResult> Couriers(CancellationToken ct) => From(await portal.CouriersAsync(ct));
    [HttpPost("couriers")] public async Task<IActionResult> AddCourier(UpsertCourierDto dto, CancellationToken ct) => From(await portal.CreateCourierAsync(dto, ct));
    [HttpPut("couriers/{id:guid}")] public async Task<IActionResult> EditCourier(Guid id, UpsertCourierDto dto, CancellationToken ct) => From(await portal.UpdateCourierAsync(id, dto, ct));
    [HttpDelete("couriers/{id:guid}")] public async Task<IActionResult> DeleteCourier(Guid id, CancellationToken ct) => From(await portal.DeleteCourierAsync(id, ct));

    [HttpGet("stats")] public async Task<IActionResult> Stats(CancellationToken ct) => From(await portal.StatsAsync(ct));
}
