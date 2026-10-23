using Dawwerli.Api.Infrastructure;
using Dawwerli.Application.Contracts;
using Dawwerli.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Dawwerli.Api.Controllers;

/// <summary>
/// Public courier link endpoints: no login, scoped to one unguessable token and rate-limited per token.
/// A token stops working when its order is delivered or cancelled, the courier changes or the page issues a new link.
/// </summary>
[Route("api/v1/track/{token}"), AllowAnonymous, EnableRateLimiting(RateLimitPolicies.Track)]
public sealed class TrackController(CourierTrackingService tracking) : AppController
{
    [HttpGet] public async Task<IActionResult> Get(string token, CancellationToken ct) => NoStore(From(await tracking.GetAsync(token, ct)));
    [HttpPost("start")] public async Task<IActionResult> Start(string token, CancellationToken ct) => NoStore(From(await tracking.StartAsync(token, ct)));
    [HttpPost("stop")] public async Task<IActionResult> Stop(string token, CancellationToken ct) => NoStore(From(await tracking.StopAsync(token, ct)));
    [HttpPost("location")]
    public async Task<IActionResult> Location(string token, TrackLocationDto dto, CancellationToken ct) => NoStore(From(await tracking.LocationAsync(token, dto, ct)));

    private IActionResult NoStore(IActionResult r)
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Robots-Tag"] = "noindex";
        return r;
    }
}
