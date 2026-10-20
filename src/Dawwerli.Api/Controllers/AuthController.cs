using Dawwerli.Api.Infrastructure;
using Dawwerli.Application.Abstractions;
using Dawwerli.Application.Contracts;
using Dawwerli.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Dawwerli.Api.Controllers;

[Route("api/v1/auth")]
[EnableRateLimiting(RateLimitPolicies.Otp)]
public sealed class AuthController(AuthService auth, ICurrentUser me) : AppController
{
    [HttpPost("otp/request")]
    public async Task<IActionResult> RequestOtp(OtpRequestDto dto, CancellationToken ct) => From(await auth.RequestOtpAsync(dto, ct));

    [HttpPost("otp/verify")]
    public async Task<IActionResult> Verify(OtpVerifyDto dto, CancellationToken ct) => From(await auth.VerifyOtpAsync(dto, ct));

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshDto dto, CancellationToken ct) => From(await auth.RefreshAsync(dto, ct));

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(RefreshDto dto, CancellationToken ct) => From(await auth.LogoutAsync(dto, ct));

    [Authorize, HttpPost("switch-role")]
    public async Task<IActionResult> SwitchRole(SwitchRoleDto dto, CancellationToken ct) => From(await auth.SwitchRoleAsync(me.UserId, dto.Role, dto.DeviceId, ct));

    [HttpPost("admin/login"), EnableRateLimiting(RateLimitPolicies.AdminLogin)]
    public async Task<IActionResult> AdminLogin(AdminLoginDto dto, CancellationToken ct) => From(await auth.AdminLoginAsync(dto, ct));
}
