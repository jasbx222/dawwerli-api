using Dawwerli.Api.Infrastructure;
using Dawwerli.Application.Contracts;
using Dawwerli.Application.Services;
using Dawwerli.Domain.Rules;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dawwerli.Api.Controllers;

[Route("api/admin/v1"), Authorize(Roles = Roles.Admin)]
public sealed class AdminController(AdminService admin) : AppController
{
    private const int AuditTake = 200;

    [HttpGet("kpis")] public async Task<IActionResult> Kpis(CancellationToken ct) => Ok(await admin.KpisAsync(ct));
    [HttpGet("pages")] public async Task<IActionResult> Pages([FromQuery] string? status, CancellationToken ct) => Ok(await admin.PagesAsync(status, ct));
    [HttpPost("pages/{id:guid}/verify")] public async Task<IActionResult> Verify(Guid id, CancellationToken ct) => From(await admin.VerifyPageAsync(id, ct));
    [HttpPost("pages/{id:guid}/reject")] public async Task<IActionResult> Reject(Guid id, ReasonDto dto, CancellationToken ct) => From(await admin.RejectPageAsync(id, dto, ct));
    [HttpPost("pages/{id:guid}/suspend")] public async Task<IActionResult> Suspend(Guid id, SuspendDto dto, CancellationToken ct) => From(await admin.SuspendPageAsync(id, dto, ct));
    [HttpPost("pages/{id:guid}/ban")] public async Task<IActionResult> Ban(Guid id, ReasonDto dto, CancellationToken ct) => From(await admin.BanPageAsync(id, dto, ct));
    [HttpPost("pages/{id:guid}/wallet/adjust")] public async Task<IActionResult> Adjust(Guid id, AdjustWalletDto dto, CancellationToken ct) => From(await admin.AdjustWalletAsync(id, dto, ct));
    [HttpGet("users")] public async Task<IActionResult> Users([FromQuery] string? role, [FromQuery] bool? banned, CancellationToken ct) => Ok(await admin.UsersAsync(role, banned, ct));
    [HttpPost("users/{id:guid}/ban")] public async Task<IActionResult> BanUser(Guid id, CancellationToken ct) => From(await admin.SetUserBanAsync(id, true, ct));
    [HttpPost("users/{id:guid}/unban")] public async Task<IActionResult> UnbanUser(Guid id, CancellationToken ct) => From(await admin.SetUserBanAsync(id, false, ct));
    [HttpGet("topups")] public async Task<IActionResult> TopUps([FromQuery] string? status, CancellationToken ct) => Ok(await admin.TopUpsAsync(status, ct));
    [HttpPost("topups/{id:guid}/approve")] public async Task<IActionResult> ApproveTopUp(Guid id, CancellationToken ct) => From(await admin.ReviewTopUpAsync(id, true, ct));
    [HttpPost("topups/{id:guid}/reject")] public async Task<IActionResult> RejectTopUp(Guid id, CancellationToken ct) => From(await admin.ReviewTopUpAsync(id, false, ct));
    [HttpGet("ads")] public async Task<IActionResult> Ads([FromQuery] string? status, CancellationToken ct) => Ok(await admin.AdsAsync(status, ct));
    [HttpPost("ads/{id:guid}/approve")] public async Task<IActionResult> ApproveAd(Guid id, CancellationToken ct) => From(await admin.ReviewAdAsync(id, true, null, ct));
    [HttpPost("ads/{id:guid}/reject")] public async Task<IActionResult> RejectAd(Guid id, ReasonDto dto, CancellationToken ct) => From(await admin.ReviewAdAsync(id, false, dto.Reason, ct));
    [HttpGet("disputes")] public async Task<IActionResult> Disputes(CancellationToken ct) => Ok(await admin.DisputesAsync(ct));
    [HttpPost("disputes/{id:guid}/resolve")] public async Task<IActionResult> Resolve(Guid id, ResolveDto dto, CancellationToken ct) => From(await admin.ResolveDisputeAsync(id, dto, ct));
    [HttpGet("reports")] public async Task<IActionResult> Reports(CancellationToken ct) => Ok(await admin.ReportsAsync(ct));
    [HttpPost("reports/{id:guid}/resolve")] public async Task<IActionResult> ResolveReport(Guid id, ResolveDto dto, CancellationToken ct) => From(await admin.ResolveReportAsync(id, dto, ct));
    [HttpGet("settings")] public async Task<IActionResult> Settings(CancellationToken ct) => Ok(await admin.SettingsAsync(ct));
    [HttpPut("settings"), HttpPut("settings/pricing")] public async Task<IActionResult> SaveSettings(BusinessRules dto, CancellationToken ct) => From(await admin.SaveSettingsAsync(dto, ct));
    [HttpPost("categories")] public async Task<IActionResult> CreateCategory(UpsertCategoryDto dto, CancellationToken ct) => From(await admin.UpsertCategoryAsync(null, dto, ct));
    [HttpPut("categories/{id:int}")] public async Task<IActionResult> UpdateCategory(int id, UpsertCategoryDto dto, CancellationToken ct) => From(await admin.UpsertCategoryAsync(id, dto, ct));
    [HttpGet("keywords")] public async Task<IActionResult> Keywords(CancellationToken ct) => Ok(await admin.KeywordsAsync(ct));
    [HttpPost("keywords")] public async Task<IActionResult> AddKeyword(KeywordDto dto, CancellationToken ct) => From(await admin.AddKeywordAsync(dto, ct));
    [HttpDelete("keywords/{id:int}")] public async Task<IActionResult> RemoveKeyword(int id, CancellationToken ct) => From(await admin.RemoveKeywordAsync(id, ct));
    [HttpGet("contact")] public async Task<IActionResult> Messages(CancellationToken ct) => Ok(await admin.MessagesAsync(ct));
    [HttpPost("contact/{id:guid}/reply")] public async Task<IActionResult> Reply(Guid id, ReasonDto dto, CancellationToken ct) => From(await admin.ReplyAsync(id, dto, ct));
    [HttpGet("audit")] public async Task<IActionResult> Audit(CancellationToken ct) => Ok(await admin.AuditAsync(AuditTake, ct));
}
