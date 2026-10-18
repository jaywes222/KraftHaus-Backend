using Microsoft.Extensions.Options;

namespace Communications.Application;

/// <summary><c>Communications</c>: broadcast throttling and delivery reports. Validated at start-up.</summary>
public sealed class CommunicationsOptions
{
    public const string SectionName = "Communications";

    /// <summary>How often the dispatcher runs (cron, UTC).</summary>
    public string DispatchCron { get; init; } = "* * * * *";

    /// <summary>
    ///     Broadcast messages sent per dispatcher run (per tenant). Keeps broadcasts well under the SMS provider's rate
    ///     so OTPs and confirmations, which do not queue, are never held up.
    /// </summary>
    public int BatchSize { get; init; } = 60;

    /// <summary>How far ahead a broadcast may be scheduled.</summary>
    public int MaxScheduleDays { get; init; } = 90;

    /// <summary>
    ///     Appended to marketing broadcasts: how to stop them (e.g. "Reply STOP to opt out" or the provider's opt-out
    ///     code). Empty means nothing is added; set it before sending marketing.
    /// </summary>
    public string MarketingFooter { get; init; } = string.Empty;

    /// <summary>
    ///     Secret in the delivery-report URL (<c>/webhooks/sms/{token}</c>) given to the SMS provider. Empty turns the
    ///     webhook off (404). At least 24 characters when set.
    /// </summary>
    public string DeliveryReportToken { get; init; } = string.Empty;

    /// <summary>Messages a log page returns at most.</summary>
    public int MaxPageSize { get; init; } = 200;
}

public sealed class CommunicationsOptionsValidator : IValidateOptions<CommunicationsOptions>
{
    public const int MinTokenLength = 24;

    public ValidateOptionsResult Validate(string? name, CommunicationsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.DispatchCron))
        {
            failures.Add("Communications:DispatchCron is required.");
        }

        if (options.BatchSize is < 1 or > 1000)
        {
            failures.Add("Communications:BatchSize must be 1-1000.");
        }

        if (options.MaxScheduleDays is < 1 or > 365)
        {
            failures.Add("Communications:MaxScheduleDays must be 1-365.");
        }

        if (options.MarketingFooter.Length > 60)
        {
            failures.Add("Communications:MarketingFooter must be at most 60 characters.");
        }

        if (options.DeliveryReportToken.Length is > 0 and < MinTokenLength)
        {
            failures.Add($"Communications:DeliveryReportToken must be at least {MinTokenLength} characters (or empty to turn the webhook off).");
        }

        if (options.MaxPageSize is < 10 or > 1000)
        {
            failures.Add("Communications:MaxPageSize must be 10-1000.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
