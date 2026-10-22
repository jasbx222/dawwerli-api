using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Dawwerli.Application.Abstractions;
using Dawwerli.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Dawwerli.Infrastructure.Services;

public static class SellerPortalClaims
{
    public const string Role = "SellerPortal";
    public const string Scope = "scope";
    public const string ScopeValue = "seller_portal";
    public const string Stamp = "pwd_stamp";
}

public sealed class SellerTokenService(IOptions<JwtOptions> jwt, IOptions<SellerPortalOptions> portal) : ISellerTokenService
{
    public int AccessTokenSeconds => portal.Value.AccessMinutes * 60;

    public string Create(SellerAccount account)
    {
        var o = jwt.Value;
        Claim[] claims =
        [
            new(JwtRegisteredClaimNames.Sub, account.Id.ToString()),
            new(ClaimTypes.Role, SellerPortalClaims.Role),
            new(SellerPortalClaims.Scope, SellerPortalClaims.ScopeValue),
            new(AppClaims.PageId, account.PageId.ToString()),
            new(SellerPortalClaims.Stamp, SellerAccount.StampOf(account.PasswordHash)),
        ];
        var creds = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.SigningKey)), SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(o.Issuer, o.Audience, claims, expires: DateTime.UtcNow.AddMinutes(portal.Value.AccessMinutes), signingCredentials: creds);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

/// <summary>Used until a gateway is wired: electronic checkout answers payment.not_configured.</summary>
public sealed class UnconfiguredPaymentProvider : IPaymentProvider
{
    public bool IsConfigured => false;
    public Task<PaymentSession> StartAsync(StoreOrder order, long amount, CancellationToken ct = default) =>
        throw new InvalidOperationException("No payment provider is configured");
}
