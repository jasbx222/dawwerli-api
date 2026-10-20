using Dawwerli.Api.Infrastructure;
using Dawwerli.Application.Contracts;
using Dawwerli.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;

namespace Dawwerli.Api.Controllers;

public sealed class MediaController(LocalStorage storage, IOptions<StorageOptions> options) : AppController
{
    private static readonly FileExtensionContentTypeProvider Types = new();
    private static readonly string[] Folders = ["requests", "offers", "receipts", "ads", "disputes", "pages"];

    /// <summary>Multipart upload; returns a storage key to send in the create call.</summary>
    [Authorize, HttpPost("api/v1/media"), RequestSizeLimit(50_000_000)]
    public async Task<IActionResult> Upload(IFormFile file, [FromForm] string kind, CancellationToken ct)
    {
        var o = options.Value;
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!Folders.Contains(kind)) return ProblemMapping.ToProblem(Domain.Common.Error.Validation("media.kind", "Unknown kind"));
        if (file.Length == 0 || file.Length > o.MaxUploadBytes) return ProblemMapping.ToProblem(Domain.Common.Error.Validation("media.size", "File too large"));
        if (!o.AllowedExtensions.Contains(ext)) return ProblemMapping.ToProblem(Domain.Common.Error.Validation("media.type", "File type not allowed"));
        await using var s = file.OpenReadStream();
        var key = await storage.SaveAsync(s, ext, kind, ct);
        return Ok(new MediaDto(key, storage.Url(key)!));
    }

    /// <summary>Signed, expiring download.</summary>
    [AllowAnonymous, HttpGet("media/{**key}")]
    public IActionResult Get(string key, [FromQuery] long exp, [FromQuery] string sig)
    {
        if (!storage.Validate(key, exp, sig)) return Forbid();
        var path = storage.PathFor(key);
        if (!System.IO.File.Exists(path)) return NotFound();
        Response.Headers.CacheControl = "private, max-age=3600";
        return PhysicalFile(path, Types.TryGetContentType(path, out var t) ? t : "application/octet-stream");
    }
}
