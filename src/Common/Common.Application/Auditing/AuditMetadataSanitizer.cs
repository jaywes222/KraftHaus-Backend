using System.Text.RegularExpressions;

namespace Common.Application.Auditing;

/// <summary>
///     Last line of defence that keeps secrets and personal data out of audit metadata (WP-08.3).
///     <para>
///         Mappers build metadata from an explicit allow-list of safe fields; this sanitiser assumes they can
///         still get it wrong. It redacts any value whose key names a sensitive concept (code, token, secret,
///         phone, e-mail, name, ...) and any value that looks like a credential or personal data regardless of
///         its key (JWT, refresh token, e-mail address, phone number, OTP-shaped digit string, long opaque secret).
///         Redacted values are replaced with <see cref="Redacted" /> rather than dropped so a leak attempt is
///         visible in review instead of silently disappearing.
///     </para>
///     <para>
///         The only sensitive-looking keys that are allowed through are keyed fingerprints
///         (<c>...Fingerprint</c>) whose value is a 64-character hex HMAC produced by
///         <see cref="IAuditFingerprinter" />.
///     </para>
/// </summary>
public static partial class AuditMetadataSanitizer
{
    public const string Redacted = "[REDACTED]";

    public const int MaxEntries = 20;

    public const int MaxValueLength = 200;

    private const string FingerprintSuffix = "fingerprint";

    private static readonly HashSet<string> _sensitiveKeyTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "otp",
        "code",
        "pin",
        "passcode",
        "password",
        "secret",
        "token",
        "jwt",
        "bearer",
        "authorization",
        "cookie",
        "hash",
        "key",
        "phone",
        "msisdn",
        "mobile",
        "email",
        "mail",
        "destination",
        "recipient",
        "name",
        "address",
        "ip"
    };

    public static IReadOnlyDictionary<string, string> Sanitize(
        IReadOnlyDictionary<string, string>? metadata)
    {
        var sanitized = new Dictionary<string, string>(StringComparer.Ordinal);

        if (metadata is null)
        {
            return sanitized;
        }

        foreach (var (key, value) in metadata.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (sanitized.Count >= MaxEntries)
            {
                break;
            }

            if (!SafeKeyRegex().IsMatch(key))
            {
                continue;
            }

            sanitized[key] = SanitizeValue(key, value);
        }

        return sanitized;
    }

    public static bool IsSensitiveKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);

        var tokens = KeyTokenRegex()
            .Matches(key)
            .Select(x => x.Value)
            .ToArray();

        if (tokens.Length > 1 &&
            string.Equals(tokens[^1], FingerprintSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return tokens.Any(_sensitiveKeyTokens.Contains);
    }

    public static bool LooksSensitive(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var trimmed = value.Trim();

        // Opaque ids are the normal currency of audit metadata and are not secrets.
        if (Guid.TryParseExact(trimmed, "D", out _))
        {
            return false;
        }

        return JwtRegex().IsMatch(trimmed) ||
               RefreshTokenRegex().IsMatch(trimmed) ||
               EmailRegex().IsMatch(trimmed) ||
               PhoneRegex().IsMatch(trimmed) ||
               OneTimeCodeRegex().IsMatch(trimmed) ||
               OpaqueSecretRegex().IsMatch(trimmed) ||
               trimmed.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeValue(
        string key,
        string? value)
    {
        if (value is null)
        {
            return string.Empty;
        }

        if (IsSensitiveKey(key))
        {
            return Redacted;
        }

        if (key.EndsWith("Fingerprint", StringComparison.OrdinalIgnoreCase))
        {
            return FingerprintRegex().IsMatch(value) ? value : Redacted;
        }

        if (LooksSensitive(value))
        {
            return Redacted;
        }

        return value.Length <= MaxValueLength
            ? value
            : value[..MaxValueLength];
    }

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeKeyRegex();

    // Splits camelCase / PascalCase keys into word tokens: "destinationFingerprint" -> destination, Fingerprint.
    [GeneratedRegex("[A-Z]?[a-z0-9]+|[A-Z]+(?![a-z])", RegexOptions.CultureInvariant)]
    private static partial Regex KeyTokenRegex();

    [GeneratedRegex(@"^eyJ[A-Za-z0-9_-]*\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex JwtRegex();

    // Opaque refresh token format: <session guid>.<generation>.<secret>
    [GeneratedRegex(@"^[0-9a-fA-F-]{36}\.\d+\.[A-Za-z0-9_-]{16,}$", RegexOptions.CultureInvariant)]
    private static partial Regex RefreshTokenRegex();

    [GeneratedRegex(@"[^@\s]+@[^@\s]+\.[^@\s]+", RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();

    // E.164-ish or local phone numbers, allowing common separators.
    [GeneratedRegex(@"^\+?[0-9][0-9 ()\-]{6,18}[0-9]$", RegexOptions.CultureInvariant)]
    private static partial Regex PhoneRegex();

    // OTP/PIN-shaped values: 4 to 8 digits on their own.
    [GeneratedRegex(@"^\d{4,8}$", RegexOptions.CultureInvariant)]
    private static partial Regex OneTimeCodeRegex();

    // Long base64/base64url/hex runs without separators are treated as secrets or hashes.
    [GeneratedRegex(@"^[A-Za-z0-9+/_=-]{32,}$", RegexOptions.CultureInvariant)]
    private static partial Regex OpaqueSecretRegex();

    [GeneratedRegex("^[0-9A-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex FingerprintRegex();
}
