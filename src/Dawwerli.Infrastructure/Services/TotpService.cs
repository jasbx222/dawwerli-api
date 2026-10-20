using System.Security.Cryptography;
using Dawwerli.Application.Abstractions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Dawwerli.Infrastructure.Services;

/// <summary>RFC 6238 TOTP (HMAC-SHA1, 30 s, 6 digits). Secrets are encrypted with ASP.NET Data Protection.</summary>
public sealed class TotpService(IDataProtectionProvider dp, IClock clock, IOptions<AdminAuthOptions> options) : ITotpService
{
    private const int StepSeconds = 30, Digits = 6, SecretBytes = 20;
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private readonly IDataProtector _protector = dp.CreateProtector("dawwerli.admin.totp");
    private readonly AdminAuthOptions _o = options.Value;

    public (string Base32, string Protected) NewSecret()
    {
        var b32 = ToBase32(RandomNumberGenerator.GetBytes(SecretBytes));
        return (b32, _protector.Protect(b32));
    }

    public string Reveal(string protectedSecret) => _protector.Unprotect(protectedSecret);

    public string OtpAuthUri(string account, string base32Secret)
    {
        var issuer = Uri.EscapeDataString(_o.TotpIssuer);
        return $"otpauth://totp/{issuer}:{Uri.EscapeDataString(account)}?secret={base32Secret}&issuer={issuer}&digits={Digits}&period={StepSeconds}";
    }

    public long? Verify(string protectedSecret, string code, long lastStep)
    {
        if (code.Length != Digits || !code.All(char.IsAsciiDigit)) return null;
        var key = FromBase32(Reveal(protectedSecret));
        var now = new DateTimeOffset(clock.UtcNow).ToUnixTimeSeconds() / StepSeconds;
        for (var d = -_o.TotpDriftSteps; d <= _o.TotpDriftSteps; d++)
        {
            var step = now + d;
            if (step > lastStep && CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(Code(key, step)), System.Text.Encoding.ASCII.GetBytes(code)))
                return step;
        }
        return null;
    }

    public static string Code(byte[] key, long step)
    {
        var msg = BitConverter.GetBytes(step);
        if (BitConverter.IsLittleEndian) Array.Reverse(msg);
        var h = HMACSHA1.HashData(key, msg);
        var o = h[^1] & 0x0F;
        var bin = ((h[o] & 0x7F) << 24) | (h[o + 1] << 16) | (h[o + 2] << 8) | h[o + 3];
        return (bin % (int)Math.Pow(10, Digits)).ToString().PadLeft(Digits, '0');
    }

    public static string ToBase32(byte[] data)
    {
        var sb = new System.Text.StringBuilder();
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b; bits += 8;
            while (bits >= 5) { sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]); bits -= 5; }
        }
        if (bits > 0) sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    public static byte[] FromBase32(string s)
    {
        var bytes = new List<byte>();
        int buffer = 0, bits = 0;
        foreach (var c in s.TrimEnd('=').ToUpperInvariant())
        {
            buffer = (buffer << 5) | Alphabet.IndexOf(c); bits += 5;
            if (bits >= 8) { bytes.Add((byte)(buffer >> (bits - 8))); bits -= 8; }
        }
        return bytes.ToArray();
    }
}
