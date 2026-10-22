using Dawwerli.Api.Infrastructure;
using Dawwerli.Application.Contracts;
using Dawwerli.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dawwerli.Api.Controllers;

/// <summary>Customer app: a page's products, top-rated pages' products for home, one-page cart, checkout and store orders.</summary>
[Route("api/v1"), Authorize(Roles = Roles.Customer)]
public sealed class StoreController(StoreService store) : AppController
{
    [AllowAnonymous, HttpGet("pages/{pageId:guid}/products")]
    public async Task<IActionResult> PageProducts(Guid pageId, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        From(await store.PageProductsAsync(pageId, page, pageSize, ct));

    [AllowAnonymous, HttpGet("products/top-rated")]
    public async Task<IActionResult> TopRated([FromQuery] int? pages, [FromQuery] int? perPage, CancellationToken ct) =>
        Ok(await store.TopRatedProductsAsync(pages, perPage, ct));

    [AllowAnonymous, HttpGet("products/{id:guid}")] public async Task<IActionResult> Product(Guid id, CancellationToken ct) => From(await store.ProductAsync(id, ct));
    [HttpPost("products/{id:guid}/report")] public async Task<IActionResult> Report(Guid id, ReportProductDto dto, CancellationToken ct) => From(await store.ReportAsync(id, dto, ct));

    [HttpGet("cart")] public async Task<IActionResult> Cart(CancellationToken ct) => Ok(await store.CartAsync(ct));
    [HttpPost("cart/items")] public async Task<IActionResult> Add(AddCartItemDto dto, CancellationToken ct) => From(await store.AddAsync(dto, ct));
    [HttpPut("cart/items/{productId:guid}")] public async Task<IActionResult> SetQty(Guid productId, SetCartQuantityDto dto, CancellationToken ct) => From(await store.SetQuantityAsync(productId, dto, ct));
    [HttpDelete("cart/items/{productId:guid}")] public async Task<IActionResult> Remove(Guid productId, CancellationToken ct) => Ok(await store.RemoveAsync(productId, ct));
    [HttpDelete("cart")] public async Task<IActionResult> Clear(CancellationToken ct) { await store.ClearAsync(ct); return NoContent(); }

    [HttpPost("store/checkout"), Idempotent] public async Task<IActionResult> Checkout(CheckoutDto dto, CancellationToken ct) => From(await store.CheckoutAsync(dto, ct));
    [HttpGet("store/orders")]
    public async Task<IActionResult> Orders([FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) => Ok(await store.MyOrdersAsync(page, pageSize, ct));
    [HttpGet("store/orders/{id:guid}")] public async Task<IActionResult> Order(Guid id, CancellationToken ct) => From(await store.MyOrderAsync(id, ct));
    [HttpPost("store/orders/{id:guid}/cancel")] public async Task<IActionResult> Cancel(Guid id, CancellationToken ct) => From(await store.CancelAsync(id, ct));
}
