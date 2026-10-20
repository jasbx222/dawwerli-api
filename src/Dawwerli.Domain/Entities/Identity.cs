using Dawwerli.Domain.Common;

namespace Dawwerli.Domain.Entities;

public class User : Entity
{
    public UserRole Role { get; set; }
    public string PhoneHash { get; set; } = "";
    public string PhoneEncrypted { get; set; } = "";
    public string? Name { get; set; }
    public int? GovernorateId { get; set; }
    public Governorate? Governorate { get; set; }
    public string? Area { get; set; }
    public string? Email { get; set; }
    public string? PasswordHash { get; set; }
    /// <summary>Consecutive wrong phone+password logins; reset on success.</summary>
    public int FailedLogins { get; set; }
    public DateTime? LoginLockedUntil { get; set; }
    public AdminRole AdminRole { get; set; }
    /// <summary>TOTP secret, encrypted at rest. Set at enrollment start; active once TotpEnabled.</summary>
    public string? TotpSecret { get; set; }
    public bool TotpEnabled { get; set; }
    /// <summary>Last accepted TOTP time step, to block code replay.</summary>
    public long TotpLastStep { get; set; }
    public DateTime? PostingBannedUntil { get; set; }
    public int BanCount { get; set; }
    public bool IsBanned { get; set; }
    public DateTime? DeletedAt { get; set; }
    public bool NeedsProfile => Name is null || GovernorateId is null;
}

public class OtpChallenge : Entity
{
    public string PhoneHash { get; set; } = "";
    public string CodeHash { get; set; } = "";
    public OtpChannel Channel { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime LastSentAt { get; set; }
    public int SendCount { get; set; }
    public int FailedSends { get; set; }
    public int WrongAttempts { get; set; }
    public DateTime? LockedUntil { get; set; }
    public bool Consumed { get; set; }
}

public class RefreshToken : Entity
{
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = "";
    public Guid FamilyId { get; set; }
    public string? DeviceId { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}

public class Device : Entity
{
    public Guid UserId { get; set; }
    public string PushToken { get; set; } = "";
    public string Platform { get; set; } = "";
    public string? AppVersion { get; set; }
}
