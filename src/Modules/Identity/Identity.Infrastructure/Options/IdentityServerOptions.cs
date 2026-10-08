using Common.Application.Validation;
using FluentValidation;

namespace Identity.Infrastructure.Options;

public sealed class KraftHausIdentityServerOptions
{
    public required string IssuerUri { get; init; }

    public required string ApiScope { get; init; }

    public required string ApiAudience { get; init; }

    public int AccessTokenLifetimeSeconds { get; init; } = 600;

    public bool RequireHttpsMetadata { get; init; } = true;
}

public sealed class KraftHausIdentityServerOptionsValidator
    : CustomValidator<KraftHausIdentityServerOptions>
{
    public KraftHausIdentityServerOptionsValidator()
    {
        RuleFor(x => x.IssuerUri)
            .NotEmpty()
            .Must(value =>
                Uri.TryCreate(
                    value,
                    UriKind.Absolute,
                    out var uri) &&
                (
                    string.Equals(
                        uri.Scheme,
                        Uri.UriSchemeHttp,
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        uri.Scheme,
                        Uri.UriSchemeHttps,
                        StringComparison.OrdinalIgnoreCase)
                ))
            .WithMessage(
                "IdentityServer IssuerUri must be an absolute HTTP or HTTPS URL.");

        RuleFor(x => x.ApiScope)
            .NotEmpty();

        RuleFor(x => x.ApiAudience)
            .NotEmpty();

        RuleFor(x => x.AccessTokenLifetimeSeconds)
            .GreaterThan(0)
            .LessThanOrEqualTo(3600);
    }
}
