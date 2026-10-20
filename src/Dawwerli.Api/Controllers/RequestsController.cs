using Dawwerli.Api.Infrastructure;
using Dawwerli.Application.Contracts;
using Dawwerli.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dawwerli.Api.Controllers;

[Route("api/v1"), Authorize(Roles = Roles.Customer)]
public sealed class RequestsController(RequestService requests, OfferService offers, OrderService orders, TrustService trust) : AppController
{
    [HttpPost("requests"), Idempotent]
    public async Task<IActionResult> Create(CreateRequestDto dto, CancellationToken ct) => From(await requests.CreateAsync(dto, ct));

    [HttpGet("requests/mine")]
    public async Task<IActionResult> Mine(CancellationToken ct) => Ok(await requests.MineAsync(ct));

    [HttpGet("requests/open-count")]
    public async Task<IActionResult> OpenCount(CancellationToken ct) => Ok(new { count = await requests.OpenRequestsCountAsync(ct) });

    [HttpGet("requests/{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct) => From(await requests.LiveAsync(id, ct));

    [HttpPost("requests/{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct) => From(await requests.CancelAsync(id, ct));

    [HttpPost("requests/{id:guid}/raise-price")]
    public async Task<IActionResult> Raise(Guid id, RaisePriceDto dto, CancellationToken ct) => From(await requests.RaisePriceAsync(id, dto, ct));

    [HttpPost("requests/{id:guid}/republish"), Idempotent]
    public async Task<IActionResult> Republish(Guid id, CancellationToken ct) => From(await requests.RepublishAsync(id, ct));

    [HttpGet("requests/{id:guid}/offers")]
    public async Task<IActionResult> Offers(Guid id, CancellationToken ct) => From(await offers.BoardAsync(id, ct));

    [HttpPost("requests/{id:guid}/select"), Idempotent]
    public async Task<IActionResult> Select(Guid id, SelectDto dto, CancellationToken ct) => From(await offers.SelectAsync(id, dto, ct));

    [HttpGet("requests/{id:guid}/selection")]
    public async Task<IActionResult> Selection(Guid id, CancellationToken ct) => From(await offers.SelectionAsync(id, ct));

    [HttpPost("offers/{id:guid}/hide")]
    public async Task<IActionResult> Hide(Guid id, CancellationToken ct) => From(await offers.SetHiddenAsync(id, true, ct));

    [HttpPost("offers/{id:guid}/unhide")]
    public async Task<IActionResult> Unhide(Guid id, CancellationToken ct) => From(await offers.SetHiddenAsync(id, false, ct));

    [HttpGet("orders/{id:guid}")]
    public async Task<IActionResult> Order(Guid id, CancellationToken ct) => From(await orders.GetAsync(id, ct));

    [HttpPost("orders/{id:guid}/no-reply")]
    public async Task<IActionResult> NoReply(Guid id, CancellationToken ct) => From(await offers.NoReplyAsync(id, ct));

    [HttpPost("orders/{id:guid}/delivery-answer")]
    public async Task<IActionResult> Answer(Guid id, DeliveryAnswerDto dto, CancellationToken ct) => From(await orders.AnswerAsync(id, dto, ct));

    [HttpPost("orders/{id:guid}/rating")]
    public async Task<IActionResult> Rate(Guid id, RatingDto dto, CancellationToken ct) => From(await orders.RateAsync(id, dto, ct));

    [HttpPost("orders/{id:guid}/complaint")]
    public async Task<IActionResult> Complaint(Guid id, ReportDto dto, CancellationToken ct) => From(await trust.ReportAsync(dto with { Target = "page", OrderId = id }, ct));
}

[Route("api/v1"), Authorize(Roles = $"{Roles.Customer},{Roles.Page}")]
public sealed class TrustController(TrustService trust) : AppController
{
    [HttpPost("reports"), Idempotent]
    public async Task<IActionResult> Report(ReportDto dto, CancellationToken ct) => From(await trust.ReportAsync(dto, ct));

    [HttpPost("blocks")]
    public async Task<IActionResult> Block(BlockDto dto, CancellationToken ct) => From(await trust.BlockAsync(dto, ct));

    [HttpGet("blocks")]
    public async Task<IActionResult> Blocked(CancellationToken ct) => Ok(await trust.BlockedAsync(ct));

    /// <summary>Unblock by page id or user id.</summary>
    [HttpDelete("blocks/{id:guid}")]
    public async Task<IActionResult> Unblock(Guid id, CancellationToken ct) => From(await trust.UnblockAsync(id, ct));

    [HttpPost("disputes"), Idempotent]
    public async Task<IActionResult> Dispute(CreateDisputeDto dto, CancellationToken ct) => From(await trust.CreateDisputeAsync(dto, ct));

    [HttpGet("disputes")]
    public async Task<IActionResult> Disputes(CancellationToken ct) => Ok(await trust.MyDisputesAsync(ct));

    [HttpGet("disputes/{id:guid}")]
    public async Task<IActionResult> GetDispute(Guid id, CancellationToken ct) => From(await trust.DisputeAsync(id, ct));
}
