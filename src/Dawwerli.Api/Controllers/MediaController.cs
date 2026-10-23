using Dawwerli.Api.Infrastructure;
using Dawwerli.Application.Abstractions;
using Dawwerli.Application.Services;
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
    private static readonly string[] Folders = ["requests", "offers", "receipts", "ads", "disputes", "pages", StoreOptions.ProductImageFolder];

    private static readonly string[] PortalFolders = [StoreOptions.ProductImageFolder, "offers", "receipts", "ads"];

    /// <summary>Multipart upload; returns a storage key to send in the create call.</summary>
    [Authorize, HttpPost("api/v1/media"), HttpPost("api/seller/v1/media"), RequestSizeLimit(50_000_000)]
    public async Task<IActionResult> Upload(IFormFile file, [FromForm] string kind, CancellationToken ct)
    {
        var o = options.Value;
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (kind == AdminService.CategoryImageFolder && !User.IsInRole(Roles.Admin)) return Forbid();
        // Product images come only from the web seller portal; portal tokens upload only what the portal sends
        // (product, offer and ad photos, top-up receipts), and only as images.
        var portal = User.IsInRole(Roles.SellerPortal);
        if (kind == StoreOptions.ProductImageFolder && !portal) return Forbid();
        if (portal && !PortalFolders.Contains(kind)) return Forbid();
        if (!Folders.Contains(kind) && kind != AdminService.CategoryImageFolder) return ProblemMapping.ToProblem(Domain.Common.Error.Validation("media.kind", "Unknown kind"));
        if (file.Length == 0 || file.Length > o.MaxUploadBytes) return ProblemMapping.ToProblem(Domain.Common.Error.Validation("media.size", "File too large"));
        if (!o.AllowedExtensions.Contains(ext)) return ProblemMapping.ToProblem(Domain.Common.Error.Validation("media.type", "File type not allowed"));
        // Product photos are images only, and an image's bytes must match its extension (no renamed HTML, SVG or scripts).
        if ((portal || kind == StoreOptions.ProductImageFolder) && !ImageExtensions.Contains(ext))
            return ProblemMapping.ToProblem(Domain.Common.Error.Validation("media.type", "Photos must be JPEG, PNG, WebP or HEIC"));
        if (ImageExtensions.Contains(ext) && !await LooksLikeImageAsync(file, ext, ct))
            return ProblemMapping.ToProblem(Domain.Common.Error.Validation("media.type", "File content does not match its type"));
        await using var s = file.OpenReadStream();
        var key = await storage.SaveAsync(s, ext, kind, ct);
        return Ok(new MediaDto(key, storage.Url(key)!));
    }

    private static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".webp", ".heic"];

    /// <summary>Magic-byte check for the image formats we accept.</summary>
    private static async Task<bool> LooksLikeImageAsync(IFormFile file, string ext, CancellationToken ct)
    {
        var head = new byte[16];
        await using var s = file.OpenReadStream();
        var n = await s.ReadAtLeastAsync(head, head.Length, throwOnEndOfStream: false, ct);
        bool At(int offset, params byte[] sig) => n >= offset + sig.Length && head.AsSpan(offset, sig.Length).SequenceEqual(sig);
        return ext switch
        {
            ".jpg" or ".jpeg" => At(0, 0xFF, 0xD8, 0xFF),
            ".png" => At(0, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A),
            ".webp" => At(0, (byte)'R', (byte)'I', (byte)'F', (byte)'F') && At(8, (byte)'W', (byte)'E', (byte)'B', (byte)'P'),
            ".heic" => At(4, (byte)'f', (byte)'t', (byte)'y', (byte)'p'),
            _ => false,
        };
    }

    /// <summary>Signed, expiring download.</summary>
    [AllowAnonymous, HttpGet("media/{**key}")]
    public IActionResult Get(string key, [FromQuery] long exp, [FromQuery] string sig)
    {
        if (!storage.Validate(key, exp, sig)) return Forbid();
        var path = storage.PathFor(key);
        if (!System.IO.File.Exists(path)) return NotFound();
        Response.Headers.CacheControl = "private, max-age=3600";
        Response.Headers.XContentTypeOptions = "nosniff";
        return PhysicalFile(path, Types.TryGetContentType(path, out var t) ? t : "application/octet-stream");
    }
}
