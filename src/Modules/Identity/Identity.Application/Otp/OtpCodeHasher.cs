using System.Security.Cryptography;
using System.Text;
using Identity.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace Identity.Application.Otp;

public sealed class OtpSecurityOptions
{
    public required string HashSecret { get; init; }
}

public sealed class OtpCodeHasher(
    IOptions<OtpSecurityOptions> options) : IOtpCodeHasher
{
    private readonly byte[] _secret = CreateSecret(options.Value);

    private static byte[] CreateSecret(OtpSecurityOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.HashSecret))
        {
            throw new InvalidOperationException(
                "Identity OTP security is not configured. " +
                "Set 'Identity:OtpSecurity:HashSecret'.");
        }

        return Encoding.UTF8.GetBytes(options.HashSecret);
    }

    public string Hash(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var input = Encoding.UTF8.GetBytes(code);
        var hash = HMACSHA256.HashData(_secret, input);

        return Convert.ToHexString(hash);
    }
}
