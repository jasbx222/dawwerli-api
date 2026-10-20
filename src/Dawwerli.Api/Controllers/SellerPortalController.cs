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
    [HttpGet("stats")] public async Task<IActionResult> Stats(CancellationToken ct) => From(await portal.StatsAsync(ct));
}
