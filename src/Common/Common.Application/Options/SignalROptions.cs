using Common.Application.Validation;
using FluentValidation;

namespace Common.Application.Options;

public class SignalROptions
{
    public bool UseRedisBackplane { get; set; }

    /// <summary>
    ///     True for a deployment that runs one API instance (Spec 08: one Windows VM, no Redis). Hub fan-out needs no
    ///     backplane then. Mirrors <see cref="CachingOptions.AllowInMemoryOnlyInProduction" />.
    /// </summary>
    public bool AllowSingleInstanceInProduction { get; set; }
    public required string RedisConnectionString { get; set; }
}

public class SignalROptionsValidator : CustomValidator<SignalROptions>
{
    public SignalROptionsValidator()
    {
        RuleFor(x => x.RedisConnectionString)
            .NotEmpty()
            .WithMessage("RedisConnectionString is required when UseRedisBackplane is enabled.")
            .When(x => x.UseRedisBackplane);

        // Multi-instance deployments require the Redis backplane for SignalR fan-out.
        RuleFor(x => x.UseRedisBackplane)
            .Must((options, useRedisBackplane, context) =>
                !context.IsProduction() || useRedisBackplane || options.AllowSingleInstanceInProduction)
            .WithMessage(
                "SignalROptions.UseRedisBackplane is false in Production. " +
                "Multi-instance deployments require the Redis backplane for SignalR fan-out; " +
                "a single-instance deployment sets AllowSingleInstanceInProduction to true.");
    }
}
