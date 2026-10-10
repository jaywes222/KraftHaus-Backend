using System.Security.Cryptography;
using System.Text;
using Common.Application.Auditing;
using Common.Application.Options;
using Microsoft.Extensions.Options;

namespace Common.Infrastructure.Auditing;

/// <summary>
///     HMAC-SHA256 fingerprint keyed with <see cref="SecurityAuditOptions.FingerprintSecret" />. Values are trimmed and
///     upper-cased first so "User@Example.com " and "user@example.com" correlate.
/// </summary>
internal sealed class HmacAuditFingerprinter(
    IOptions<SecurityAuditOptions> options)
    : IAuditFingerprinter
{
    private readonly byte[] _key = Encoding.UTF8.GetBytes(options.Value.FingerprintSecret);

    public string Fingerprint(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var normalized = value.Trim().ToUpperInvariant();

        return Convert.ToHexString(
            HMACSHA256.HashData(
                _key,
                Encoding.UTF8.GetBytes(normalized)));
    }
}
