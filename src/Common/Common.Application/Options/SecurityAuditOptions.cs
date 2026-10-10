using Common.Application.Validation;
using FluentValidation;

namespace Common.Application.Options;

/// <summary>
///     Security audit settings (Spec 08 WP-08.3).
/// </summary>
public sealed class SecurityAuditOptions
{
    /// <summary>
    ///     Secret used to fingerprint personal values (phone numbers, e-mail addresses) in audit records.
    ///     Treat like any other secret: environment variable or protected config only, never committed for
    ///     production. Rotating it breaks correlation with fingerprints written under the previous secret.
    /// </summary>
    public required string FingerprintSecret { get; init; }
}

public sealed class SecurityAuditOptionsValidator : CustomValidator<SecurityAuditOptions>
{
    public const int MinimumSecretLength = 32;

    public SecurityAuditOptionsValidator()
    {
        RuleFor(x => x.FingerprintSecret)
            .NotEmpty()
            .MinimumLength(MinimumSecretLength)
            .WithMessage(
                $"SecurityAuditOptions.FingerprintSecret must be at least {MinimumSecretLength} characters.");
    }
}
