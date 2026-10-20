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

    [HttpGet("kpis")] public async Task<IActionResult> Kpis(CancellationToken ct) => Ok(await admin.KpisAsync(ct));
    [HttpGet("pages")] public async Task<IActionResult> Pages([FromQuery] string? status, CancellationToken ct) => Ok(await admin.PagesAsync(status, ct));
    [Authorize(Roles = AdminRoles.Moderation), HttpPost("pages/{id:guid}/verify")] public async Task<IActionResult> Verify(Guid id, CancellationToken ct) => From(await admin.VerifyPageAsync(id, ct));
    [Authorize(Roles = AdminRoles.Moderation), HttpPost("pages/{id:guid}/reject")] public async Task<IActionResult> Reject(Guid id, ReasonDto dto, CancellationToken ct) => From(await admin.RejectPageAsync(id, dto, ct));
    [Authorize(Roles = AdminRoles.Moderation), HttpPost("pages/{id:guid}/suspend")] public async Task<IActionResult> Suspend(Guid id, SuspendDto dto, CancellationToken ct) => From(await admin.SuspendPageAsync(id, dto, ct));
    [Authorize(Roles = AdminRoles.Moderation), HttpPost("pages/{id:guid}/ban")] public async Task<IActionResult> Ban(Guid id, ReasonDto dto, CancellationToken ct) => From(await admin.BanPageAsync(id, dto, ct));
    [Authorize(Roles = AdminRoles.Finance), HttpPost("pages/{id:guid}/wallet/adjust")] public async Task<IActionResult> Adjust(Guid id, AdjustWalletDto dto, CancellationToken ct) => From(await admin.AdjustWalletAsync(id, dto, ct));
    [Authorize(Roles = AdminRoles.Moderation), HttpGet("users")] public async Task<IActionResult> Users([FromQuery] string? role, [FromQuery] bool? banned, CancellationToken ct) => Ok(await admin.UsersAsync(role, banned, ct));
    [Authorize(Roles = AdminRoles.Moderation), HttpPost("users/{id:guid}/ban")] public async Task<IActionResult> BanUser(Guid id, CancellationToken ct) => From(await admin.SetUserBanAsync(id, true, ct));
    [Authorize(Roles = AdminRoles.Moderation), HttpPost("users/{id:guid}/unban")] public async Task<IActionResult> UnbanUser(Guid id, CancellationToken ct) => From(await admin.SetUserBanAsync(id, false, ct));
    [Authorize(Roles = AdminRoles.Finance), HttpGet("topups")] public async Task<IActionResult> TopUps([FromQuery] string? status, CancellationToken ct) => Ok(await admin.TopUpsAsync(status, ct));
    [Authorize(Roles = AdminRoles.Finance), HttpPost("topups/{id:guid}/approve")] public async Task<IActionResult> ApproveTopUp(Guid id, CancellationToken ct) => From(await admin.ReviewTopUpAsync(id, true, ct));
    [Authorize(Roles = AdminRoles.Finance), HttpPost("topups/{id:guid}/reject")] public async Task<IActionResult> RejectTopUp(Guid id, CancellationToken ct) => From(await admin.ReviewTopUpAsync(id, false, ct));
    [Authorize(Roles = AdminRoles.Moderation), HttpGet("ads")] public async Task<IActionResult> Ads([FromQuery] string? status, CancellationToken ct) => Ok(await admin.AdsAsync(status, ct));
    [Authorize(Roles = AdminRoles.Moderation), HttpPost("ads/{id:guid}/approve")] public async Task<IActionResult> ApproveAd(Guid id, CancellationToken ct) => From(await admin.ReviewAdAsync(id, true, null, ct));
    [Authorize(Roles = AdminRoles.Moderation), HttpPost("ads/{id:guid}/reject")] public async Task<IActionResult> RejectAd(Guid id, ReasonDto dto, CancellationToken ct) => From(await admin.ReviewAdAsync(id, false, dto.Reason, ct));
    [Authorize(Roles = AdminRoles.Moderation), HttpGet("disputes")] public async Task<IActionResult> Disputes(CancellationToken ct) => Ok(await admin.DisputesAsync(ct));
    [Authorize(Roles = AdminRoles.Moderation), HttpPost("disputes/{id:guid}/resolve")] public async Task<IActionResult> Resolve(Guid id, ResolveDto dto, CancellationToken ct) => From(await admin.ResolveDisputeAsync(id, dto, ct));
    [Authorize(Roles = AdminRoles.Moderation), HttpGet("reports")] public async Task<IActionResult> Reports(CancellationToken ct) => Ok(await admin.ReportsAsync(ct));
    [Authorize(Roles = AdminRoles.Moderation), HttpPost("reports/{id:guid}/resolve")] public async Task<IActionResult> ResolveReport(Guid id, ResolveDto dto, CancellationToken ct) => From(await admin.ResolveReportAsync(id, dto, ct));
    [HttpGet("settings")] public async Task<IActionResult> Settings(CancellationToken ct) => Ok(await admin.SettingsAsync(ct));
    [Authorize(Roles = AdminRoles.Super), HttpPut("settings")] public async Task<IActionResult> SaveSettings(BusinessRules dto, CancellationToken ct) => From(await admin.SaveSettingsAsync(dto, ct));
    [Authorize(Roles = AdminRoles.Super), HttpGet("categories")] public async Task<IActionResult> Categories(CancellationToken ct) => Ok(await admin.CategoriesAsync(ct));
    [Authorize(Roles = AdminRoles.Super), HttpPost("categories")] public async Task<IActionResult> CreateCategory(UpsertCategoryDto dto, CancellationToken ct) => From(await admin.UpsertCategoryAsync(null, dto, ct));
    [Authorize(Roles = AdminRoles.Super), HttpPut("categories/{id:int}")] public async Task<IActionResult> UpdateCategory(int id, UpsertCategoryDto dto, CancellationToken ct) => From(await admin.UpsertCategoryAsync(id, dto, ct));
    [Authorize(Roles = AdminRoles.Moderation), HttpGet("keywords")] public async Task<IActionResult> Keywords(CancellationToken ct) => Ok(await admin.KeywordsAsync(ct));
    [Authorize(Roles = AdminRoles.Moderation), HttpPost("keywords")] public async Task<IActionResult> AddKeyword(KeywordDto dto, CancellationToken ct) => From(await admin.AddKeywordAsync(dto, ct));
    [Authorize(Roles = AdminRoles.Moderation), HttpDelete("keywords/{id:int}")] public async Task<IActionResult> RemoveKeyword(int id, CancellationToken ct) => From(await admin.RemoveKeywordAsync(id, ct));
    [Authorize(Roles = AdminRoles.Moderation), HttpGet("contact")] public async Task<IActionResult> Messages(CancellationToken ct) => Ok(await admin.MessagesAsync(ct));
    [Authorize(Roles = AdminRoles.Moderation), HttpPost("contact/{id:guid}/reply")] public async Task<IActionResult> Reply(Guid id, ReasonDto dto, CancellationToken ct) => From(await admin.ReplyAsync(id, dto, ct));
    [Authorize(Roles = AdminRoles.Super), HttpGet("audit")]
    public async Task<IActionResult> Audit([FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? action, CancellationToken ct) =>
        Ok(await admin.AuditAsync(page, pageSize, action, ct));
    [Authorize(Roles = AdminRoles.Finance), HttpPut("settings/pricing")] public async Task<IActionResult> SavePricing(PricingDto dto, CancellationToken ct) => From(await admin.SavePricingAsync(dto, ct));
    [Authorize(Roles = AdminRoles.Moderation), HttpPost("pages/{id:guid}/unsuspend")] public async Task<IActionResult> Unsuspend(Guid id, ReasonDto dto, CancellationToken ct) => From(await admin.UnsuspendPageAsync(id, dto, ct));
    [HttpGet("me")] public IActionResult Me() => Ok(new { adminRole = AdminRoles.Of(User) });
    [Authorize(Roles = AdminRoles.Super), HttpGet("admins")] public async Task<IActionResult> Admins(CancellationToken ct) => Ok(await admin.AdminsAsync(ct));
    [Authorize(Roles = AdminRoles.Super), HttpPost("admins")] public async Task<IActionResult> CreateAdmin(CreateAdminDto dto, CancellationToken ct) => From(await admin.CreateAdminAsync(dto, ct));
    [Authorize(Roles = AdminRoles.Super), HttpPut("admins/{id:guid}/role")] public async Task<IActionResult> SetAdminRole(Guid id, AdminRoleDto dto, CancellationToken ct) => From(await admin.SetAdminRoleAsync(id, dto, ct));
    [Authorize(Roles = AdminRoles.Super), HttpPost("admins/{id:guid}/reset-2fa")] public async Task<IActionResult> ResetTotp(Guid id, CancellationToken ct) => From(await admin.ResetAdminTotpAsync(id, ct));
    [Authorize(Roles = AdminRoles.Super), HttpPost("admins/{id:guid}/disable")] public async Task<IActionResult> DisableAdmin(Guid id, CancellationToken ct) => From(await admin.SetAdminDisabledAsync(id, true, ct));
    [Authorize(Roles = AdminRoles.Super), HttpPost("admins/{id:guid}/enable")] public async Task<IActionResult> EnableAdmin(Guid id, CancellationToken ct) => From(await admin.SetAdminDisabledAsync(id, false, ct));
}
