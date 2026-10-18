using Microsoft.Extensions.Options;

namespace Common.Infrastructure.Messaging;

/// <summary>Outbox dispatcher settings (<c>Outbox</c>).</summary>
public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>Run the dispatcher in this process (API today, the Worker service later).</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Longest wait between scans when nothing signals new rows.</summary>
    public int PollIntervalMilliseconds { get; init; } = 2000;

    public int BatchSize { get; init; } = 50;

    /// <summary>After this many failed deliveries the row is parked (dead-lettered) for an operator.</summary>
    public int MaxAttempts { get; init; } = 10;

    /// <summary>First retry delay; doubles per attempt, capped at <see cref="MaxRetryDelaySeconds" />.</summary>
    public int BaseRetryDelaySeconds { get; init; } = 5;

    public int MaxRetryDelaySeconds { get; init; } = 600;
}

public sealed class OutboxOptionsValidator : IValidateOptions<OutboxOptions>
{
    public ValidateOptionsResult Validate(string? name, OutboxOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.PollIntervalMilliseconds is < 100 or > 60_000)
        {
            failures.Add("Outbox:PollIntervalMilliseconds must be between 100 and 60000.");
        }

        if (options.BatchSize is < 1 or > 1000)
        {
            failures.Add("Outbox:BatchSize must be between 1 and 1000.");
        }

        if (options.MaxAttempts is < 1 or > 100)
        {
            failures.Add("Outbox:MaxAttempts must be between 1 and 100.");
        }

        if (options.BaseRetryDelaySeconds < 1 || options.MaxRetryDelaySeconds < options.BaseRetryDelaySeconds)
        {
            failures.Add("Outbox:BaseRetryDelaySeconds must be at least 1 and not above MaxRetryDelaySeconds.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
