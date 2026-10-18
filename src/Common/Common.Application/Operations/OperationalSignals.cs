using Microsoft.Extensions.Options;

namespace Common.Application.Operations;

/// <summary>
///     Something an operator should know about now (Spec 08 §14 business alerts): payments stuck, messages failing,
///     events not being delivered. Each module that can go wrong in a way users feel implements one; Communications
///     checks them on a schedule and texts / e-mails the operators. Summaries carry counts, never personal data.
/// </summary>
public interface IOperationalSignal
{
    Task<IReadOnlyList<OperationalIssue>> CheckAsync(CancellationToken cancellationToken);
}

/// <param name="Key">Stable id of the condition (e.g. <c>payments.stuck</c>): repeat alerts for the same key wait for the cooldown.</param>
/// <param name="Summary">One short line for an SMS: what and how many.</param>
public sealed record OperationalIssue(string Key, string Summary);

/// <summary><c>Alerts</c>: who gets operator alerts and when a condition counts as a problem. Validated at start-up.</summary>
public sealed class OperationalAlertOptions
{
    public const string SectionName = "Alerts";

    /// <summary>Operators' phones (E.164). Empty with no e-mails means alerts are only logged.</summary>
    public IReadOnlyList<string> Phones { get; init; } = [];

    public IReadOnlyList<string> Emails { get; init; } = [];

    /// <summary>How often the checks run (cron, UTC).</summary>
    public string Cron { get; init; } = "*/5 * * * *";

    /// <summary>The same condition is not re-sent within this many minutes.</summary>
    public int CooldownMinutes { get; init; } = 60;

    /// <summary>A payment still waiting for M-Pesa after this many minutes counts as stuck.</summary>
    public int StuckPaymentMinutes { get; init; } = 5;

    /// <summary>An outbox message or an expired hold left unprocessed this long counts as a backlog.</summary>
    public int BacklogMinutes { get; init; } = 10;

    /// <summary>SMS failure rate over the last hour that raises an alert (0.2 = 20%).</summary>
    public decimal SmsFailureRate { get; init; } = 0.2m;

    /// <summary>Fewer SMS than this in the hour and the failure rate is not judged.</summary>
    public int SmsMinimumSample { get; init; } = 20;
}

public sealed class OperationalAlertOptionsValidator : IValidateOptions<OperationalAlertOptions>
{
    public ValidateOptionsResult Validate(string? name, OperationalAlertOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Cron))
        {
            failures.Add("Alerts:Cron is required.");
        }

        if (options.Phones.Any(x => !x.StartsWith("+254", StringComparison.Ordinal) || x.Length != 13))
        {
            failures.Add("Alerts:Phones must be Kenyan numbers in E.164 (+2547XXXXXXXX).");
        }

        if (options.Emails.Any(x => !x.Contains('@', StringComparison.Ordinal)))
        {
            failures.Add("Alerts:Emails must be e-mail addresses.");
        }

        if (options.CooldownMinutes is < 1 or > 1440 ||
            options.StuckPaymentMinutes is < 1 or > 240 ||
            options.BacklogMinutes is < 1 or > 240 ||
            options.SmsFailureRate is <= 0m or > 1m ||
            options.SmsMinimumSample is < 1 or > 10_000)
        {
            failures.Add("Alerts: CooldownMinutes 1-1440, StuckPaymentMinutes 1-240, BacklogMinutes 1-240, SmsFailureRate (0,1], SmsMinimumSample 1-10000.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
