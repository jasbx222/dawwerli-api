using System.Security.Cryptography;
using Dawwerli.Application.Abstractions;
using Dawwerli.Application.Contracts;
using Dawwerli.Domain.Common;
using Dawwerli.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Dawwerli.Application.Services;

public sealed class AuthService(IAppDbContext db, IClock clock, IRulesProvider rulesProvider, IPhoneProtector phones,
    ISmsSender sms, ITokenService tokens, IPasswordHasher passwords, IOptions<OtpOptions> otpOptions)
{
    public async Task<Result<OtpSentDto>> RequestOtpAsync(OtpRequestDto dto, CancellationToken ct)
    {
        var rules = await rulesProvider.GetAsync(ct);
        var now = clock.UtcNow;
        var hash = phones.Hash(dto.Phone);
        var ch = await db.OtpChallenges.Where(c => c.PhoneHash == hash && !c.Consumed)
            .OrderByDescending(c => c.CreatedAt).FirstOrDefaultAsync(ct);

        if (ch?.LockedUntil > now) return Locked(ch.LockedUntil.Value, now);
        if (ch is not null && ch.LastSentAt.AddSeconds(rules.OtpResendSec) > now)
            return Error.Limited("otp.resend", "Wait before resending",
                (int)Math.Ceiling((ch.LastSentAt.AddSeconds(rules.OtpResendSec) - now).TotalSeconds));

        if (ch is null || ch.ExpiresAt <= now)
        {
            ch = new OtpChallenge { PhoneHash = hash, FailedSends = ch?.FailedSends ?? 0 };
            db.OtpChallenges.Add(ch);
        }
        if (dto.SmsNotReceived) ch.FailedSends++;
        ch.Channel = ch.FailedSends >= rules.OtpSmsFailuresBeforeWhatsApp ? OtpChannel.WhatsApp : OtpChannel.Sms;

        var code = RandomNumberGenerator.GetInt32(0, (int)Math.Pow(10, rules.OtpLength)).ToString().PadLeft(rules.OtpLength, '0');
        ch.CodeHash = tokens.HashToken(hash + code);
        ch.ExpiresAt = now.AddSeconds(rules.OtpExpirySec);
        ch.LastSentAt = now;
        ch.SendCount++;
        ch.WrongAttempts = 0;
        if (!await sms.SendOtpAsync(dto.Phone, code, ch.Channel, ct)) ch.FailedSends++;
        await db.SaveChangesAsync(ct);
        return new OtpSentDto(ch.Channel.Code(), rules.OtpExpirySec, rules.OtpResendSec,
            otpOptions.Value.ExposeCodeInResponse ? code : null);
    }

    public async Task<Result<AuthResultDto>> VerifyOtpAsync(OtpVerifyDto dto, CancellationToken ct)
    {
        if (!EnumText.TryParse<UserRole>(dto.Role, out var role) || role == UserRole.Admin) return Errors.BadEnum("role");
        var rules = await rulesProvider.GetAsync(ct);
        var now = clock.UtcNow;
        var hash = phones.Hash(dto.Phone);
        var ch = await db.OtpChallenges.Where(c => c.PhoneHash == hash && !c.Consumed)
            .OrderByDescending(c => c.CreatedAt).FirstOrDefaultAsync(ct);
        if (ch is null) return Error.Rule("otp.expired", "Request a new code");
        if (ch.LockedUntil > now) return Locked(ch.LockedUntil.Value, now);
        if (ch.ExpiresAt <= now) return Error.Rule("otp.expired", "Code expired");

        if (!CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.UTF8.GetBytes(ch.CodeHash),
                System.Text.Encoding.UTF8.GetBytes(tokens.HashToken(hash + dto.Code))))
        {
            ch.WrongAttempts++;
            if (ch.WrongAttempts >= rules.OtpMaxWrong) ch.LockedUntil = now.AddMinutes(rules.OtpLockMin);
            await db.SaveChangesAsync(ct);
            if (ch.LockedUntil > now) return Locked(ch.LockedUntil.Value, now);
            return new Error(ErrorKind.BusinessRule, "otp.wrong", "Wrong code",
                new Dictionary<string, object?> { ["attemptsLeft"] = rules.OtpMaxWrong - ch.WrongAttempts });
        }

        ch.Consumed = true;
        var user = await db.Users.FirstOrDefaultAsync(u => u.PhoneHash == hash, ct);
        if (user is { IsBanned: true }) return Error.Forbidden("account.banned", "Account is banned");
        if (user is null)
        {
            user = new User { PhoneHash = hash, PhoneEncrypted = phones.Encrypt(dto.Phone) };
            db.Users.Add(user);
        }
        user.Role = role;
        var page = await db.Pages.FirstOrDefaultAsync(p => p.UserId == user.Id, ct);
        var issued = await IssueAsync(user, page?.Id, Guid.NewGuid(), dto.DeviceId, ct);
        return new AuthResultDto(issued.AccessToken, issued.RefreshToken, issued.ExpiresIn,
            role == UserRole.Customer ? user.NeedsProfile : page is null, role.Code(), page?.Status.Code());
    }

    public async Task<Result<AuthResultDto>> RefreshAsync(RefreshDto dto, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var hash = tokens.HashToken(dto.RefreshToken);
        var rt = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (rt is null || rt.ExpiresAt <= now) return Error.Unauthorized("auth.refresh_invalid", "Refresh token invalid");
        if (rt.RevokedAt is not null)
        {
            await RevokeFamilyAsync(rt.FamilyId, ct);
            return Error.Unauthorized("auth.refresh_reused", "Refresh token reuse detected");
        }
        rt.RevokedAt = now;
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == rt.UserId && u.DeletedAt == null, ct);
        if (user is null || user.IsBanned) return Error.Unauthorized("auth.user", "User unavailable");
        var page = await db.Pages.FirstOrDefaultAsync(p => p.UserId == user.Id, ct);
        var issued = await IssueAsync(user, page?.Id, rt.FamilyId, dto.DeviceId ?? rt.DeviceId, ct);
        return new AuthResultDto(issued.AccessToken, issued.RefreshToken, issued.ExpiresIn, user.NeedsProfile, user.Role.Code(), page?.Status.Code());
    }

    /// <summary>Buyer ⇄ seller switch for the same phone account; issues tokens with the new role.</summary>
    public async Task<Result<AuthResultDto>> SwitchRoleAsync(Guid userId, string roleText, string? deviceId, CancellationToken ct)
    {
        if (!EnumText.TryParse<UserRole>(roleText, out var role) || role == UserRole.Admin) return Errors.BadEnum("role");
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.DeletedAt == null, ct);
        if (user is null || user.IsBanned) return Error.Unauthorized("auth.user", "User unavailable");
        user.Role = role;
        var page = await db.Pages.FirstOrDefaultAsync(p => p.UserId == user.Id, ct);
        var issued = await IssueAsync(user, page?.Id, Guid.NewGuid(), deviceId, ct);
        return new AuthResultDto(issued.AccessToken, issued.RefreshToken, issued.ExpiresIn,
            role == UserRole.Customer ? user.NeedsProfile : page is null, role.Code(), page?.Status.Code());
    }

    public async Task<Result> LogoutAsync(RefreshDto dto, CancellationToken ct)
    {
        var hash = tokens.HashToken(dto.RefreshToken);
        var rt = await db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (rt is not null) await RevokeFamilyAsync(rt.FamilyId, ct);
        return Result.Ok();
    }

    public async Task<Result<AuthResultDto>> AdminLoginAsync(AdminLoginDto dto, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == dto.Email && u.Role == UserRole.Admin, ct);
        if (user?.PasswordHash is null || !passwords.Verify(dto.Password, user.PasswordHash))
            return Error.Unauthorized("auth.credentials", "Wrong email or password");
        var issued = await IssueAsync(user, null, Guid.NewGuid(), null, ct);
        return new AuthResultDto(issued.AccessToken, issued.RefreshToken, issued.ExpiresIn, false, user.Role.Code(), null);
    }

    private async Task<IssuedTokens> IssueAsync(User user, Guid? pageId, Guid family, string? deviceId, CancellationToken ct)
    {
        var refresh = tokens.NewOpaqueToken();
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id, FamilyId = family, DeviceId = deviceId, TokenHash = tokens.HashToken(refresh),
            ExpiresAt = clock.UtcNow.AddDays(tokens.RefreshTokenDays),
        });
        await db.SaveChangesAsync(ct);
        return new IssuedTokens(tokens.CreateAccessToken(user, pageId), refresh, tokens.AccessTokenSeconds);
    }

    private async Task RevokeFamilyAsync(Guid family, CancellationToken ct)
    {
        var now = clock.UtcNow;
        await db.RefreshTokens.Where(t => t.FamilyId == family && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
        await db.SaveChangesAsync(ct);
    }

    private static Error Locked(DateTime until, DateTime now) =>
        Error.Limited("otp.locked", "Too many wrong codes", (int)Math.Ceiling((until - now).TotalSeconds));
}
