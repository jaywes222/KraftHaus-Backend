using Microsoft.Extensions.Options;
using Ticketing.Domain.Waitlist;

namespace Ticketing.Application.Waitlist;

/// <summary>Operator-tunable waitlist settings (<c>Ticketing:Waitlist</c>).</summary>
public sealed class WaitlistOptions
{
    public const string SectionName = "Ticketing:Waitlist";

    /// <summary>
    ///     Youngest age accepted. PLACEHOLDER until the product owner confirms it; keep it equal to the client's
    ///     <c>VITE_WAITLIST_MIN_AGE</c>.
    /// </summary>
    public int MinimumAge { get; init; } = 18;

    /// <summary>How long the request waits for the SMS gateway before answering with <c>smsSent: false</c>.</summary>
    public int ConfirmationSmsTimeoutSeconds { get; init; } = 8;

    /// <summary>Cron of the job that resends confirmations the gateway did not accept (UTC).</summary>
    public string ResendCron { get; init; } = "*/15 * * * *";

    /// <summary>Wait this long after joining before the resend job retries (the join itself tried once).</summary>
    public int ResendAfterMinutes { get; init; } = 5;

    /// <summary>Give up after this many failed attempts, or this many days after joining.</summary>
    public int MaxConfirmationAttempts { get; init; } = 5;

    public int ResendWindowDays { get; init; } = 7;

    public int ResendBatchSize { get; init; } = 100;
}

public sealed class WaitlistOptionsValidator : IValidateOptions<WaitlistOptions>
{
    public ValidateOptionsResult Validate(string? name, WaitlistOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.MinimumAge is < 1 or > WaitlistEntry.MaxAge)
        {
            failures.Add($"{WaitlistOptions.SectionName}:MinimumAge must be between 1 and {WaitlistEntry.MaxAge}.");
        }

        if (options.ConfirmationSmsTimeoutSeconds is < 1 or > 30)
        {
            failures.Add($"{WaitlistOptions.SectionName}:ConfirmationSmsTimeoutSeconds must be between 1 and 30.");
        }

        if (options.ResendAfterMinutes is < 1 or > 1440 || options.MaxConfirmationAttempts is < 1 or > 20 ||
            options.ResendWindowDays is < 1 or > 60 || options.ResendBatchSize is < 1 or > 1000)
        {
            failures.Add($"{WaitlistOptions.SectionName}: ResendAfterMinutes (1-1440), MaxConfirmationAttempts (1-20), ResendWindowDays (1-60) or ResendBatchSize (1-1000) is out of range.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
