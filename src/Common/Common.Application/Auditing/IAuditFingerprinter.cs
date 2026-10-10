namespace Common.Application.Auditing;

/// <summary>
///     Produces a stable, keyed, non-reversible fingerprint of a personal value (phone number, e-mail address)
///     so audit records can be correlated ("every OTP sent to this number") without storing the value itself.
///     Investigators fingerprint the value they are looking for with the same key and search for the result.
/// </summary>
public interface IAuditFingerprinter
{
    /// <summary>Returns the upper-case hex HMAC-SHA256 of the normalised value (64 characters).</summary>
    string Fingerprint(string value);
}
