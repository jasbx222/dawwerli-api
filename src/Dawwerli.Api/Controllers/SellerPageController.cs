using Dawwerli.Api.Infrastructure;
using Dawwerli.Application.Contracts;
using Dawwerli.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dawwerli.Api.Controllers;

/// <summary>
/// The page side of the web seller portal: offers, wallet, ads and standing for the seller account's page.
/// Same services and rules as the app's page routes (api/v1/pages/me/*); see <see cref="ActAsSellerPageAttribute"/>.
/// </summary>
[Route("api/seller/v1/page"), Authorize(Policy = Roles.SellerPortalPolicy), ActAsSellerPage]
public sealed class SellerPageController(PageService pages, OfferService offers, OrderService orders, WalletService wallet, AdsService ads) : AppController
{
    private const int LedgerPageSize = 50;

    [HttpGet] public async Task<IActionResult> Me(CancellationToken ct) => From(await pages.MeAsync(ct));
    [HttpGet("settings")] public async Task<IActionResult> Settings(CancellationToken ct) => From(await pages.SettingsAsync(ct));
    [HttpPut("settings")] public async Task<IActionResult> SaveSettings(PageSettingsDto dto, CancellationToken ct) => From(await pages.SaveSettingsAsync(dto, ct));

    [HttpGet("inbox")] public async Task<IActionResult> Inbox(CancellationToken ct) => From(await pages.InboxAsync(ct));
    [HttpGet("feed")]
    public async Task<IActionResult> Feed([FromQuery] int? categoryId, [FromQuery] string? since, [FromQuery] string? sort,
        [FromQuery] long? minPrice, [FromQuery] long? maxPrice, [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken ct) =>
        From(await pages.FeedAsync(new PageFeedQuery(categoryId, since, sort, minPrice, maxPrice, page, pageSize), ct));
    [HttpPost("requests/{id:guid}/offers"), Idempotent]
    public async Task<IActionResult> Offer(Guid id, SubmitOfferDto dto, CancellationToken ct) => From(await offers.SubmitAsync(id, dto, ct));
    [HttpPost("offers/{id:guid}/withdraw")] public async Task<IActionResult> Withdraw(Guid id, CancellationToken ct) => From(await offers.WithdrawAsync(id, ct));
    [HttpGet("offers")] public async Task<IActionResult> MyOffers([FromQuery] string? state, CancellationToken ct) => From(await pages.MyOffersAsync(ct, state));
    [HttpPost("orders/{id:guid}/contacted")] public async Task<IActionResult> Contacted(Guid id, CancellationToken ct) => From(await orders.ContactedAsync(id, ct));
    [HttpPost("orders/{id:guid}/report-ghost")] public async Task<IActionResult> Ghost(Guid id, CancellationToken ct) => From(await orders.ReportGhostAsync(id, ct));

    [HttpGet("wallet")] public async Task<IActionResult> Wallet(CancellationToken ct) => From(await wallet.WalletAsync(ct));
    [HttpGet("ledger")] public async Task<IActionResult> Ledger([FromQuery] string? cursor, CancellationToken ct) => From(await wallet.LedgerAsync(cursor, LedgerPageSize, ct));
    [HttpPost("topups"), Idempotent] public async Task<IActionResult> TopUp(CreateTopUpDto dto, CancellationToken ct) => From(await wallet.TopUpAsync(dto, ct));

    [HttpGet("ads")] public async Task<IActionResult> Ads(CancellationToken ct) => From(await ads.MineAsync(ct));
    [HttpGet("ads/pricing")] public async Task<IActionResult> AdPricing(CancellationToken ct) => Ok(await ads.PricingAsync(ct));
    [HttpGet("ads/eligibility")] public async Task<IActionResult> Eligibility(CancellationToken ct) => From(await ads.EligibilityAsync(ct));
    [HttpPost("ads"), Idempotent] public async Task<IActionResult> CreateAd(CreateAdDto dto, CancellationToken ct) => From(await ads.CreateAsync(dto, ct));
    [HttpPost("ads/{id:guid}/pause")] public async Task<IActionResult> PauseAd(Guid id, CancellationToken ct) => From(await ads.PauseAsync(id, ct));

    [HttpGet("standing")] public async Task<IActionResult> Standing(CancellationToken ct) => From(await pages.StandingAsync(ct));
    [HttpGet("ratings")] public async Task<IActionResult> Ratings(CancellationToken ct) => From(await pages.RatingsAsync(ct));
}
