using Dawwerli.Api.Infrastructure;
using Dawwerli.Application.Contracts;
using Dawwerli.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dawwerli.Api.Controllers;

[Route("api/v1"), Authorize(Roles = Roles.Page)]
public sealed class PagesController(PageService pages, OfferService offers, OrderService orders, WalletService wallet, AdsService ads,
    PageStoreOrderService storeOrders, CourierService couriers) : AppController
{
    private const int LedgerPageSize = 50;

    [HttpPost("pages/register")]
    public async Task<IActionResult> Register(RegisterPageDto dto, CancellationToken ct) => From(await pages.RegisterAsync(dto, ct));

    [HttpGet("pages/me")]
    public async Task<IActionResult> Me(CancellationToken ct) => From(await pages.MeAsync(ct));

    [HttpPost("pages/me/resubmit")]
    public async Task<IActionResult> Resubmit(CancellationToken ct) => From(await pages.ResubmitAsync(ct));

    [HttpGet("pages/me/settings"), HttpGet("page/settings")]
    public async Task<IActionResult> Settings(CancellationToken ct) => From(await pages.SettingsAsync(ct));

    [HttpPut("pages/me/settings"), HttpPut("page/settings")]
    public async Task<IActionResult> SaveSettings(PageSettingsDto dto, CancellationToken ct) => From(await pages.SaveSettingsAsync(dto, ct));

    [HttpGet("pages/me/inbox")]
    public async Task<IActionResult> Inbox(CancellationToken ct) => From(await pages.InboxAsync(ct));

    [HttpPost("requests/{id:guid}/offers"), Idempotent]
    public async Task<IActionResult> Offer(Guid id, SubmitOfferDto dto, CancellationToken ct) => From(await offers.SubmitAsync(id, dto, ct));

    [HttpPost("offers/{id:guid}/withdraw")]
    public async Task<IActionResult> Withdraw(Guid id, CancellationToken ct) => From(await offers.WithdrawAsync(id, ct));

    [HttpGet("pages/me/offers"), HttpGet("pages/me/orders")]
    public async Task<IActionResult> MyOffers([FromQuery] string? state, CancellationToken ct) => From(await pages.MyOffersAsync(ct, state));

    /// <summary>Paged, filtered home feed of live requests (see PageService.FeedAsync).</summary>
    [HttpGet("pages/me/feed")]
    public async Task<IActionResult> Feed([FromQuery] int? categoryId, [FromQuery] string? since, [FromQuery] string? sort,
        [FromQuery] long? minPrice, [FromQuery] long? maxPrice, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        From(await pages.FeedAsync(new PageFeedQuery(categoryId, since, sort, minPrice, maxPrice, page, pageSize), ct));

    /// <summary>Product orders placed with this page's store. status: a store status code, or "active" / "closed".</summary>
    [HttpGet("pages/me/store-orders")]
    public async Task<IActionResult> StoreOrders([FromQuery] string? status, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        From(await storeOrders.ListAsync(status, page, pageSize, ct));

    [HttpGet("pages/me/store-orders/{id:guid}")]
    public async Task<IActionResult> StoreOrder(Guid id, CancellationToken ct) => From(await storeOrders.GetAsync(id, ct));

    [HttpPost("pages/me/store-orders/{id:guid}/status")]
    public async Task<IActionResult> StoreOrderStatus(Guid id, SetOrderStatusDto dto, CancellationToken ct) =>
        From(await storeOrders.SetStatusAsync(id, dto, ct));

    [HttpGet("pages/me/couriers")]
    public async Task<IActionResult> Couriers(CancellationToken ct) => From(await couriers.ListAsync(ct));

    [HttpPost("pages/me/couriers")]
    public async Task<IActionResult> AddCourier(UpsertCourierDto dto, CancellationToken ct) => From(await couriers.CreateAsync(dto, ct));

    [HttpPut("pages/me/couriers/{id:guid}")]
    public async Task<IActionResult> EditCourier(Guid id, UpsertCourierDto dto, CancellationToken ct) => From(await couriers.UpdateAsync(id, dto, ct));

    [HttpDelete("pages/me/couriers/{id:guid}")]
    public async Task<IActionResult> DeleteCourier(Guid id, CancellationToken ct) => From(await couriers.DeleteAsync(id, ct));

    [HttpPost("orders/{id:guid}/contacted")]
    public async Task<IActionResult> Contacted(Guid id, CancellationToken ct) => From(await orders.ContactedAsync(id, ct));

    [HttpPost("orders/{id:guid}/report-ghost")]
    public async Task<IActionResult> Ghost(Guid id, CancellationToken ct) => From(await orders.ReportGhostAsync(id, ct));

    [HttpPost("orders/{id:guid}/report-customer")]
    public async Task<IActionResult> ReportCustomer(Guid id, ReportDto dto, CancellationToken ct) => From(await orders.ReportCustomerAsync(id, dto, ct));

    [HttpGet("pages/me/wallet")]
    public async Task<IActionResult> Wallet(CancellationToken ct) => From(await wallet.WalletAsync(ct));

    [HttpGet("pages/me/ledger")]
    public async Task<IActionResult> Ledger([FromQuery] string? cursor, CancellationToken ct) => From(await wallet.LedgerAsync(cursor, LedgerPageSize, ct));

    [HttpPost("pages/me/topups"), HttpPost("page/topups"), Idempotent]
    public async Task<IActionResult> TopUp(CreateTopUpDto dto, CancellationToken ct) => From(await wallet.TopUpAsync(dto, ct));

    [HttpPost("pages/me/subscriptions/early-access")]
    public async Task<IActionResult> EarlyOn(CancellationToken ct) => From(await wallet.SetEarlyAccessAsync(true, pages, ct));

    [HttpDelete("pages/me/subscriptions/early-access")]
    public async Task<IActionResult> EarlyOff(CancellationToken ct) => From(await wallet.SetEarlyAccessAsync(false, pages, ct));

    [HttpGet("pages/me/ads"), HttpGet("page/ads")]
    public async Task<IActionResult> Ads(CancellationToken ct) => From(await ads.MineAsync(ct));

    [HttpGet("pages/me/ads/eligibility")]
    public async Task<IActionResult> Eligibility(CancellationToken ct) => From(await ads.EligibilityAsync(ct));

    [HttpPost("pages/me/ads"), HttpPost("page/ads"), Idempotent]
    public async Task<IActionResult> CreateAd(CreateAdDto dto, CancellationToken ct) => From(await ads.CreateAsync(dto, ct));

    [HttpPost("pages/me/ads/{id:guid}/pause"), HttpPost("page/ads/{id:guid}/pause")]
    public async Task<IActionResult> PauseAd(Guid id, CancellationToken ct) => From(await ads.PauseAsync(id, ct));

    [HttpGet("pages/me/standing")]
    public async Task<IActionResult> Standing(CancellationToken ct) => From(await pages.StandingAsync(ct));

    [HttpGet("pages/me/ratings")]
    public async Task<IActionResult> Ratings(CancellationToken ct) => From(await pages.RatingsAsync(ct));
}

[Route("api/v1"), AllowAnonymous]
public sealed class DiscoverController(HomeService home, PageService pages, AdsService ads) : AppController
{
    private const int FeaturedTake = 20;
    private const int FeedTake = 50;

    [HttpGet("home")]
    public async Task<IActionResult> Home([FromQuery] int? governorateId, CancellationToken ct) => Ok(await home.LoadAsync(governorateId, ct));

    [HttpGet("home/feed")]
    public async Task<IActionResult> Feed([FromQuery] int? governorateId, CancellationToken ct) => Ok(await home.FeedAsync(governorateId, FeedTake, ct));

    [HttpGet("pages/{id:guid}")]
    public async Task<IActionResult> Page(Guid id, CancellationToken ct) => From(await pages.PublicAsync(id, ct));

    [HttpGet("pages/featured")]
    public async Task<IActionResult> Featured([FromQuery] int categoryId, [FromQuery] int governorateId, CancellationToken ct) =>
        From(await pages.FeaturedAsync(categoryId, governorateId, FeaturedTake, ct));

    [HttpGet("ads"), HttpGet("ads/slots")]
    public async Task<IActionResult> Ads([FromQuery] string slot, [FromQuery] int governorateId, [FromQuery] int? categoryId, CancellationToken ct) =>
        Ok(await ads.ServeAsync(slot, governorateId, categoryId, ct));

    [HttpPost("ads/{id:guid}/events")]
    public async Task<IActionResult> AdEvent(Guid id, AdEventDto dto, CancellationToken ct) => From(await ads.TrackAsync(id, dto, ct));
}
