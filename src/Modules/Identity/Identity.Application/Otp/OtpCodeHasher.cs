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
    private readonly byte[] _secret =
        Encoding.UTF8.GetBytes(options.Value.HashSecret);

    public string Hash(string code)
    {
        var input = Encoding.UTF8.GetBytes(code);

        var hash = HMACSHA256.HashData(_secret, input);

        return Convert.ToHexString(hash);
    }
}
