using Common.Application.Validation;
using FluentValidation;

namespace Identity.Infrastructure.Options;

public sealed class IdentityDatabaseOptions
{
    public required string PlatformConnectionString { get; init; }
}

public sealed class IdentityDatabaseOptionsValidator
    : CustomValidator<IdentityDatabaseOptions>
{
    public IdentityDatabaseOptionsValidator()
    {
        RuleFor(x => x.PlatformConnectionString)
            .NotEmpty();
    }
}
