using Common.Application.Validation;
using FluentValidation;

namespace Common.Application.Options;

public enum EmailProvider
{
    Dummy,
    Brevo,
    ZeptoMail
}

[OptionsSection("Notifications:Email")]
public sealed class EmailOptions
{
    public required EmailProvider Provider { get; init; }

    public required string BaseUrl { get; init; }

    public required string ApiKey { get; init; }

    public required string FromAddress { get; init; }

    public required string FromName { get; init; }

    public int AttemptTimeoutSeconds { get; init; } = 10;

    public int TotalRequestTimeoutSeconds { get; init; } = 15;

    public int MaxRetryAttempts { get; init; } = 1;

    public required EmailTemplatesOptions Templates { get; init; }
}

public sealed class EmailTemplatesOptions
{
    public Dictionary<string, EmailTemplateOptions> Otp { get; } = [];
}

public sealed class EmailTemplateOptions
{
    public required string Subject { get; init; }

    public required string TextBody { get; init; }

    public string? HtmlBody { get; init; }
}

public sealed class EmailOptionsValidator
    : CustomValidator<EmailOptions>
{
    public EmailOptionsValidator()
    {
        RuleFor(x => x.BaseUrl)
            .NotEmpty()
            .Must(x =>
                Uri.TryCreate(
                    x,
                    UriKind.Absolute,
                    out _))
            .WithMessage(
                "BaseUrl must be a valid absolute URL.");

        RuleFor(x => x.ApiKey)
            .NotEmpty()
            .When(x =>
                x.Provider != EmailProvider.Dummy);

        RuleFor(x => x.FromAddress)
            .NotEmpty()
            .EmailAddress();

        RuleFor(x => x.FromName)
            .NotEmpty();

        RuleFor(x => x.AttemptTimeoutSeconds)
            .GreaterThan(0);

        RuleFor(x => x.TotalRequestTimeoutSeconds)
            .GreaterThanOrEqualTo(
                x => x.AttemptTimeoutSeconds);

        RuleFor(x => x.MaxRetryAttempts)
            .GreaterThanOrEqualTo(0);

        RuleFor(x => x.Provider)
            .Must((_, provider, context) =>
                !context.IsProduction() ||
                provider != EmailProvider.Dummy)
            .WithMessage(
                "Dummy email provider cannot be used in Production.");
    }
}
