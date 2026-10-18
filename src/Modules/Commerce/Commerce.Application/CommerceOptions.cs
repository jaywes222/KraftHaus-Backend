using Microsoft.Extensions.Options;

namespace Commerce.Application;

/// <summary>Commerce tunables (<c>Commerce</c> section).</summary>
public sealed class CommerceOptions
{
    public const string SectionName = "Commerce";

    /// <summary>Reservations (pre-orders) close this long before the event's first date starts.</summary>
    public int ReservationsCloseHoursBeforeStart { get; init; } = 2;

    /// <summary>Live ordering opens this long before the first date starts (doors) and closes when the last date ends.</summary>
    public int LiveOrderingOpensMinutesBeforeStart { get; init; } = 60;

    /// <summary>How long portions are held while the M-Pesa prompt is on the phone.</summary>
    public int HoldMinutes { get; init; } = 10;

    /// <summary>Grace for a prompt still on the phone when its hold runs out.</summary>
    public int PendingPaymentGraceMinutes { get; init; } = 6;

    public int MaxPortionsPerOrder { get; init; } = 20;

    /// <summary>At most this many prompts per order.</summary>
    public int MaxPaymentAttempts { get; init; } = 5;

    /// <summary>A Ready order not collected after this long is flagged on the kitchen board.</summary>
    public int UncollectedAfterMinutes { get; init; } = 20;

    public string ReleaseHoldsCron { get; init; } = "* * * * *";

    public string UncollectedCron { get; init; } = "* * * * *";

    public int ReleaseBatchSize { get; init; } = 200;

    /// <summary>Kraft logos and menu photos; relative paths are under the API's content root. Back it up.</summary>
    public string ImagesRootPath { get; init; } = "App_Data/commerce-images";

    public int MaxImageBytes { get; init; } = 3 * 1024 * 1024;
}

public sealed class CommerceOptionsValidator : IValidateOptions<CommerceOptions>
{
    public ValidateOptionsResult Validate(string? name, CommerceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.ReservationsCloseHoursBeforeStart is < 0 or > 168)
        {
            failures.Add("Commerce:ReservationsCloseHoursBeforeStart must be 0-168.");
        }

        if (options.LiveOrderingOpensMinutesBeforeStart is < 0 or > 1440)
        {
            failures.Add("Commerce:LiveOrderingOpensMinutesBeforeStart must be 0-1440.");
        }

        if (options.HoldMinutes is < 2 or > 60 || options.PendingPaymentGraceMinutes is < 0 or > 30)
        {
            failures.Add("Commerce:HoldMinutes must be 2-60 and PendingPaymentGraceMinutes 0-30.");
        }

        if (string.IsNullOrWhiteSpace(options.ImagesRootPath) || options.MaxImageBytes is < 50 * 1024 or > 20 * 1024 * 1024)
        {
            failures.Add("Commerce:ImagesRootPath is required and MaxImageBytes must be 51200-20971520.");
        }

        if (options.MaxPortionsPerOrder is < 1 or > 100 || options.MaxPaymentAttempts is < 1 or > 10)
        {
            failures.Add("Commerce:MaxPortionsPerOrder must be 1-100 and MaxPaymentAttempts 1-10.");
        }

        if (options.UncollectedAfterMinutes is < 1 or > 240)
        {
            failures.Add("Commerce:UncollectedAfterMinutes must be 1-240.");
        }

        if (string.IsNullOrWhiteSpace(options.ReleaseHoldsCron) || string.IsNullOrWhiteSpace(options.UncollectedCron))
        {
            failures.Add("Commerce:ReleaseHoldsCron and UncollectedCron are required.");
        }

        if (options.ReleaseBatchSize is < 1 or > 1000)
        {
            failures.Add("Commerce:ReleaseBatchSize must be 1-1000.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
