using Dawwerli.Api.Infrastructure;
using Dawwerli.Application.Contracts;
using Dawwerli.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Dawwerli.Api.Controllers;

[Route("api/v1"), Authorize]
public sealed class AccountController(AccountService account, NotificationService notifications) : AppController
{
    private const int DefaultPageSize = 30;

    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct) => From(await account.MeAsync(ct));

    [HttpPut("me/profile")]
    public async Task<IActionResult> Profile(ProfileDto dto, CancellationToken ct) => From(await account.SaveProfileAsync(dto, ct));

    [HttpGet("me/standing")]
    public async Task<IActionResult> Standing(CancellationToken ct) => From(await account.StandingAsync(ct));

    [HttpDelete("me")]
    public async Task<IActionResult> Delete(CancellationToken ct) => From(await account.DeleteAsync(ct));

    [HttpPost("devices")]
    public async Task<IActionResult> Device(DeviceDto dto, CancellationToken ct) => From(await account.RegisterDeviceAsync(dto, ct));

    [HttpGet("notifications")]
    public async Task<IActionResult> Notifications([FromQuery] string? cursor, CancellationToken ct) =>
        Ok(await notifications.ListAsync(cursor, DefaultPageSize, ct));

    [HttpPost("notifications/read-all")]
    public async Task<IActionResult> ReadAll(CancellationToken ct)
    {
        await notifications.ReadAllAsync(ct);
        return NoContent();
    }
}

[Route("api/v1"), AllowAnonymous]
public sealed class PublicController(CatalogService catalog, IWebHostEnvironment env) : AppController
{
    [HttpGet("categories"), ResponseCache(Duration = 300)]
    public async Task<IActionResult> Categories(CancellationToken ct) => Ok(await catalog.CategoriesAsync(ct));

    [HttpGet("governorates"), ResponseCache(Duration = 3600)]
    public async Task<IActionResult> Governorates(CancellationToken ct) => Ok(await catalog.GovernoratesAsync(ct));

    [HttpPost("categories/suggest")]
    public async Task<IActionResult> Suggest(SuggestDto dto, CancellationToken ct) => Ok(await catalog.SuggestAsync(dto.Text, ct));

    [HttpGet("config")]
    [HttpGet("settings/public")]
    public async Task<IActionResult> Config(CancellationToken ct) => Ok(await catalog.PublicConfigAsync(ct));

    [HttpGet("legal/{doc:regex(^(privacy|terms)$)}")]
    public IActionResult Legal(string doc)
    {
        var file = env.ContentRootFileProvider.GetFileInfo($"Content/{doc}.md");
        return file.Exists ? PhysicalFile(file.PhysicalPath!, "text/markdown; charset=utf-8") : NotFound();
    }
}
