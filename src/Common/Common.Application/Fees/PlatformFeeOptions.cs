using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Common.Application.Fees;

/// <summary>
///     The customer-paid platform fee (<c>PlatformFees</c>), in whole shillings: added on top of the price at checkout and
///     waived for accounts whose phone is on the event's waitlist (<see cref="Ticketing.IWaitlistMembership" />).
///     Ticketing charges <see cref="TicketFeePerTicket" /> per admission; Commerce charges <see cref="FoodOrderFee" /> on
///     each Kraft order (a cart split across two Krafts pays it twice).
/// </summary>
public sealed class PlatformFeeOptions
{
    public const string SectionName = "PlatformFees";

    /// <summary>KES per admission (a Couple bundle is two admissions). 0 turns the ticket fee off.</summary>
    public int TicketFeePerTicket { get; init; } = 50;

    /// <summary>KES per Kraft order (food and drinks, reservations included). 0 turns the food fee off.</summary>
    public int FoodOrderFee { get; init; } = 30;

    /// <summary>Waitlisted phones pay no platform fee on that event.</summary>
    public bool WaiveForWaitlist { get; init; } = true;

    public const int MaxFee = 10_000;
}

public sealed class PlatformFeeOptionsValidator : IValidateOptions<PlatformFeeOptions>
{
    public ValidateOptionsResult Validate(string? name, PlatformFeeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.TicketFeePerTicket is < 0 or > PlatformFeeOptions.MaxFee)
        {
            failures.Add($"PlatformFees:TicketFeePerTicket must be between 0 and {PlatformFeeOptions.MaxFee}.");
        }

        if (options.FoodOrderFee is < 0 or > PlatformFeeOptions.MaxFee)
        {
            failures.Add($"PlatformFees:FoodOrderFee must be between 0 and {PlatformFeeOptions.MaxFee}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}

public static class PlatformFeeRegistration
{
    /// <summary>Safe to call from every module that charges a fee.</summary>
    public static IServiceCollection AddPlatformFees(this IServiceCollection services)
    {
        services.AddOptions<PlatformFeeOptions>().BindConfiguration(PlatformFeeOptions.SectionName).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<PlatformFeeOptions>, PlatformFeeOptionsValidator>());

        return services;
    }
}
