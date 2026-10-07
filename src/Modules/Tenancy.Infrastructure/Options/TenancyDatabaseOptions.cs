using Common.Application.Validation;
using FluentValidation;

namespace Tenancy.Infrastructure.Options;

public sealed class TenancyDatabaseOptions
{
    public required string PlatformConnectionString { get; init; }
    public required string AdminConnectionString { get; init; }
    public string TenantDatabaseNamePrefix { get; init; } = "krafthaus_t_";
}

public sealed class TenancyDatabaseOptionsValidator : CustomValidator<TenancyDatabaseOptions>
{
    public TenancyDatabaseOptionsValidator()
    {
        RuleFor(x => x.PlatformConnectionString).NotEmpty();
        RuleFor(x => x.AdminConnectionString).NotEmpty();
        RuleFor(x => x.TenantDatabaseNamePrefix)
            .NotEmpty()
            .Matches("^[a-z][a-z0-9_]{0,30}$");
    }
}
