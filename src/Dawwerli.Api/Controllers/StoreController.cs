using Dawwerli.Api.Infrastructure;
using Dawwerli.Application.Contracts;
using Dawwerli.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dawwerli.Api.Controllers;

/// <summary>Customer app: a page's products, top-rated pages' products for home, one-page cart, checkout and store orders.</summary>
[Route("api/v1"), Authorize(Roles = Roles.Customer)]
public sealed class StoreController(StoreService store, DiscoveryService discover, CourierTrackingService tracking) : AppController
{
    /// <summary>Live courier position while the courier shares it ({active:false} otherwise). Distance/ETA are approximate.</summary>
    [HttpGet("store/orders/{id:guid}/courier-location")]
    public async Task<IActionResult> CourierLocation(Guid id, CancellationToken ct) => From(await tracking.ForCustomerAsync(id, ct));

    [AllowAnonymous, HttpGet("pages/{pageId:guid}/products")]
    public async Task<IActionResult> PageProducts(Guid pageId, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        From(await store.PageProductsAsync(pageId, page, pageSize, ct));

    [AllowAnonymous, HttpGet("products/top-rated")]
    public async Task<IActionResult> TopRated([FromQuery] int? pages, [FromQuery] int? perPage, CancellationToken ct) =>
        Ok(await store.TopRatedProductsAsync(pages, perPage, ct));

    /// <summary>Home «الأكثر مبيعاً»: the app shows the first 10 and opens the rest with «عرض المزيد».</summary>
    [AllowAnonymous, HttpGet("products/best-selling")]
    public async Task<IActionResult> BestSelling([FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        Ok(await discover.BestSellingAsync(page, pageSize, ct));

    /// <summary>Home «الأعلى تقييماً», by each product's own rating.</summary>
    [AllowAnonymous, HttpGet("products/highest-rated")]
    public async Task<IActionResult> HighestRated([FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        Ok(await discover.HighestRatedAsync(page, pageSize, ct));

    [AllowAnonymous, HttpGet("pages/top")]
    public async Task<IActionResult> TopPages([FromQuery] int? take, CancellationToken ct) => Ok(await discover.TopPagesAsync(take, ct));

    /// <summary>The pages tab. following=true lists only pages I follow (needs sign-in).</summary>
    [AllowAnonymous, HttpGet("pages/directory")]
    public async Task<IActionResult> Directory([FromQuery] string? q, [FromQuery] int? categoryId, [FromQuery] bool following,
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        Ok(await discover.DirectoryAsync(q, categoryId, following, page, pageSize, ct));

    [AllowAnonymous, HttpGet("pages/{id:guid}/profile")]
    public async Task<IActionResult> Profile(Guid id, CancellationToken ct) => From(await discover.ProfileAsync(id, ct));

    [HttpPost("pages/{id:guid}/follow")] public async Task<IActionResult> Follow(Guid id, CancellationToken ct) => From(await discover.FollowAsync(id, ct));
    [HttpDelete("pages/{id:guid}/follow")] public async Task<IActionResult> Unfollow(Guid id, CancellationToken ct) => From(await discover.UnfollowAsync(id, ct));

    [AllowAnonymous, HttpGet("products/{id:guid}")] public async Task<IActionResult> Product(Guid id, CancellationToken ct) => From(await store.ProductAsync(id, ct));
    [HttpGet("favorites")] public async Task<IActionResult> Favorites(CancellationToken ct) => Ok(await store.FavoritesAsync(ct));
    [HttpPut("products/{id:guid}/favorite")] public async Task<IActionResult> Favorite(Guid id, CancellationToken ct) => From(await store.AddFavoriteAsync(id, ct));
    [HttpDelete("products/{id:guid}/favorite")] public async Task<IActionResult> Unfavorite(Guid id, CancellationToken ct) { await store.RemoveFavoriteAsync(id, ct); return NoContent(); }

    [AllowAnonymous, HttpGet("products/{id:guid}/ratings")]
    public async Task<IActionResult> Ratings(Guid id, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        From(await store.ReviewsAsync(id, page, pageSize, ct));
    [HttpGet("products/{id:guid}/ratings/mine")] public async Task<IActionResult> MyRating(Guid id, CancellationToken ct) => From(await store.MyRatingAsync(id, ct));
    /// <summary>Rate once (stars + optional comment). A second rating is 409 rating.duplicate; edit with PUT.</summary>
    [HttpPost("products/{id:guid}/ratings")] public async Task<IActionResult> CreateRating(Guid id, RateProductDto dto, CancellationToken ct) => From(await store.CreateRatingAsync(id, dto, ct));
    [HttpPut("products/{id:guid}/rating")] public async Task<IActionResult> Rate(Guid id, RateProductDto dto, CancellationToken ct) => From(await store.RateAsync(id, dto, ct));

    [HttpPost("products/{id:guid}/report")] public async Task<IActionResult> Report(Guid id, ReportProductDto dto, CancellationToken ct) => From(await store.ReportAsync(id, dto, ct));

    /// <summary>The cart also answers under store/ (store/cart, store/cart/items), next to store/checkout and store/orders; the customer app uses those paths.</summary>
    [HttpGet("cart"), HttpGet("store/cart")] public async Task<IActionResult> Cart(CancellationToken ct) => Ok(await store.CartAsync(ct));
    [HttpPost("cart/items"), HttpPost("store/cart/items")] public async Task<IActionResult> Add(AddCartItemDto dto, CancellationToken ct) => From(await store.AddAsync(dto, ct));
    [HttpPut("cart/items/{productId:guid}"), HttpPut("store/cart/items/{productId:guid}")] public async Task<IActionResult> SetQty(Guid productId, SetCartQuantityDto dto, CancellationToken ct) => From(await store.SetQuantityAsync(productId, dto, ct));
    [HttpDelete("cart/items/{productId:guid}"), HttpDelete("store/cart/items/{productId:guid}")] public async Task<IActionResult> Remove(Guid productId, CancellationToken ct) => Ok(await store.RemoveAsync(productId, ct));
    [HttpDelete("cart"), HttpDelete("store/cart")] public async Task<IActionResult> Clear(CancellationToken ct) { await store.ClearAsync(ct); return NoContent(); }

    /// <summary>Place the cart as an order. POST store/orders is the same action (the customer app posts there).</summary>
    [HttpPost("store/checkout"), HttpPost("store/orders"), Idempotent] public async Task<IActionResult> Checkout(CheckoutDto dto, CancellationToken ct) => From(await store.CheckoutAsync(dto, ct));
    [HttpGet("store/orders")]
    public async Task<IActionResult> Orders([FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) => Ok(await store.MyOrdersAsync(page, pageSize, ct));
    [HttpGet("store/orders/{id:guid}")] public async Task<IActionResult> Order(Guid id, CancellationToken ct) => From(await store.MyOrderAsync(id, ct));
    [HttpPost("store/orders/{id:guid}/cancel")] public async Task<IActionResult> Cancel(Guid id, CancellationToken ct) => From(await store.CancelAsync(id, ct));
}
