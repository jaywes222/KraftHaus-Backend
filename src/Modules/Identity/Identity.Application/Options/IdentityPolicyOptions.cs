using Common.Application.Validation;
using FluentValidation;

namespace Identity.Application.Options;

public sealed class IdentityPolicyOptions
{
    public const string SectionName =
        "Identity:Policies";

    public required PolicyOptions Terms { get; init; }

    public required PolicyOptions Privacy { get; init; }
}

public sealed class PolicyOptions
{
    public required string Key { get; init; }

    public int Version { get; init; }

    public required string TextHash { get; init; }
}

public sealed class IdentityPolicyOptionsValidator
    : CustomValidator<IdentityPolicyOptions>
{
    public IdentityPolicyOptionsValidator()
    {
        RuleFor(x => x.Terms.Key)
            .NotEmpty();

        RuleFor(x => x.Terms.Version)
            .GreaterThan(0);

        RuleFor(x => x.Terms.TextHash)
            .NotEmpty();

        RuleFor(x => x.Privacy.Key)
            .NotEmpty();

        RuleFor(x => x.Privacy.Version)
            .GreaterThan(0);

        RuleFor(x => x.Privacy.TextHash)
            .NotEmpty();
    }
}
