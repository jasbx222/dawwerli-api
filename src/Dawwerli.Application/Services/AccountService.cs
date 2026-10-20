using Dawwerli.Application.Abstractions;
using Dawwerli.Application.Contracts;
using Dawwerli.Domain.Common;
using Dawwerli.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Dawwerli.Application.Services;

public sealed class AccountService(Shared s, IPhoneProtector phones, IStorage storage)
{
    private IAppDbContext Db => s.Db;

    public async Task<Result<MeDto>> MeAsync(CancellationToken ct)
    {
        var u = await Db.Users.Include(x => x.Governorate).FirstOrDefaultAsync(x => x.Id == s.Me.UserId, ct);
        if (u is null) return Error.NotFound("user");
        var page = await Db.Pages.FirstOrDefaultAsync(p => p.UserId == u.Id, ct);
        return new MeDto(u.Id, s.Me.Role.Code(), u.Name, u.GovernorateId, u.Governorate?.Name, u.Area,
            phones.Decrypt(u.PhoneEncrypted), page?.Id, page?.Status.Code());
    }

    public async Task<Result> SaveProfileAsync(ProfileDto dto, CancellationToken ct)
    {
        if (!await Db.Governorates.AnyAsync(g => g.Id == dto.GovernorateId, ct)) return Error.NotFound("governorate");
        var u = await Db.Users.FirstAsync(x => x.Id == s.Me.UserId, ct);
        u.Name = dto.Name.Trim();
        u.GovernorateId = dto.GovernorateId;
        u.Area = dto.Area.Trim();
        await Db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<Result> SaveRegionAsync(RegionDto dto, CancellationToken ct)
    {
        if (!await Db.Governorates.AnyAsync(g => g.Id == dto.GovernorateId, ct)) return Error.NotFound("governorate");
        var u = await Db.Users.FirstAsync(x => x.Id == s.Me.UserId, ct);
        u.GovernorateId = dto.GovernorateId;
        u.Area = dto.Area.Trim();
        await Db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<Result<IdDto>> ContactAsync(ContactDto dto, CancellationToken ct)
    {
        var m = new SupportMessage { UserId = s.Me.UserId, Topic = dto.Topic.Trim(), Text = dto.Text.Trim(), Status = CaseStatus.Open };
        Db.SupportMessages.Add(m);
        await Db.SaveChangesAsync(ct);
        return new IdDto(m.Id);
    }

    public async Task<Result<StandingDto>> StandingAsync(CancellationToken ct)
    {
        var rules = await s.Rules(ct);
        var since = s.Now.AddDays(-rules.StrikeWindowDays);
        var u = await Db.Users.FirstAsync(x => x.Id == s.Me.UserId, ct);
        var strikes = await Db.Strikes.Where(x => x.UserId == u.Id && x.CreatedAt >= since)
            .OrderByDescending(x => x.CreatedAt).Select(x => new StrikeDto(x.Id, x.Reason, x.CreatedAt, x.Frozen)).ToListAsync(ct);
        var bannedUntil = u.PostingBannedUntil > s.Now ? u.PostingBannedUntil : null;
        return new StandingDto(strikes, rules.StrikesForBan, bannedUntil, u.IsBanned);
    }

    public async Task<Result> RegisterDeviceAsync(DeviceDto dto, CancellationToken ct)
    {
        var d = await Db.Devices.FirstOrDefaultAsync(x => x.PushToken == dto.PushToken, ct);
        if (d is null) Db.Devices.Add(d = new Device { PushToken = dto.PushToken });
        d.UserId = s.Me.UserId;
        d.Platform = dto.Platform;
        d.AppVersion = dto.AppVersion;
        await Db.SaveChangesAsync(ct);
        return Result.Ok();
    }

    /// <summary>Hard-erases personal data; keeps anonymized financial/order rows.</summary>
    public async Task<string> MyPhoneAsync(CancellationToken ct) =>
        phones.Decrypt(await Db.Users.Where(u => u.Id == s.Me.UserId).Select(u => u.PhoneEncrypted).FirstAsync(ct));

    public async Task<Result> DeleteAsync(CancellationToken ct)
    {
        var id = s.Me.UserId;
        var openOrders = await Db.Orders.AnyAsync(o => (o.CustomerId == id || o.Page!.UserId == id) &&
            (o.Status == OrderStatus.InPurchase || o.Status == OrderStatus.AwaitingDelivery), ct);
        if (openOrders) return Error.Conflict("account.open_orders", "Close open orders first");

        await using var tx = await Db.BeginTransactionAsync(ct);
        var u = await Db.Users.FirstAsync(x => x.Id == id, ct);
        var media = await Db.Requests.Where(r => r.CustomerId == id && r.MediaKey != null).Select(r => r.MediaKey!).ToListAsync(ct);
        await Db.Requests.Where(r => r.CustomerId == id && (r.Status == RequestStatus.Open || r.Status == RequestStatus.Expanded || r.Status == RequestStatus.Locked))
            .ExecuteUpdateAsync(x => x.SetProperty(r => r.Status, RequestStatus.Cancelled), ct);
        await Db.Requests.Where(r => r.CustomerId == id)
            .ExecuteUpdateAsync(x => x.SetProperty(r => r.Description, "").SetProperty(r => r.MediaKey, (string?)null), ct);
        var page = await Db.Pages.FirstOrDefaultAsync(p => p.UserId == id, ct);
        if (page is not null)
        {
            media.AddRange(await Db.Offers.Where(o => o.PageId == page.Id).Select(o => o.PhotoKey).ToListAsync(ct));
            await Db.Offers.Where(o => o.PageId == page.Id).ExecuteUpdateAsync(x => x.SetProperty(o => o.PhotoKey, "").SetProperty(o => o.Note, ""), ct);
            page.Name = "";
            page.InstagramHandle = $"deleted-{page.Id:N}";
            page.Bio = null;
            if (page.ImageKey is not null) media.Add(page.ImageKey);
            page.ImageKey = null;
            page.Status = PageStatus.Banned;
        }
        await Db.Devices.Where(d => d.UserId == id).ExecuteDeleteAsync(ct);
        await Db.RefreshTokens.Where(t => t.UserId == id).ExecuteDeleteAsync(ct);
        await Db.Notifications.Where(n => n.UserId == id).ExecuteDeleteAsync(ct);
        u.Name = null;
        u.Area = null;
        u.PhoneEncrypted = "";
        u.PhoneHash = $"deleted:{u.Id:N}";
        u.DeletedAt = s.Now;
        await Db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        foreach (var key in media.Where(k => !string.IsNullOrEmpty(k))) await storage.DeleteAsync(key, ct);
        return Result.Ok();
    }
}
