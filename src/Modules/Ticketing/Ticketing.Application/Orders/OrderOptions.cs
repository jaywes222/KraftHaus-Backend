using Microsoft.Extensions.Options;

namespace Ticketing.Application.Orders;

/// <summary>Operator-tunable checkout settings (<c>Ticketing:Orders</c>). Hold length is per event (Events policies).</summary>
public sealed class OrderOptions
{
    public const string SectionName = "Ticketing:Orders";

    /// <summary>M-Pesa prompts one order may send (each retry is a new prompt).</summary>
    public int MaxPaymentAttempts { get; init; } = 5;

    /// <summary>Orders a customer may have waiting for payment on one event at a time (stops seat hoarding).</summary>
    public int MaxOpenOrdersPerCustomer { get; init; } = 1;

    /// <summary>
    ///     How long past the hold an order whose prompt is still pending is kept before it is expired anyway. Should
    ///     exceed <c>Payments:PendingTimeoutMinutes</c>.
    /// </summary>
    public int PendingPaymentGraceMinutes { get; init; } = 6;

    /// <summary>Cron of the job that releases expired holds (UTC).</summary>
    public string ReleaseHoldsCron { get; init; } = "* * * * *";

    /// <summary>Orders expired per tenant per job run.</summary>
    public int ReleaseBatchSize { get; init; } = 200;
}

public sealed class OrderOptionsValidator : IValidateOptions<OrderOptions>
{
    public ValidateOptionsResult Validate(string? name, OrderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.MaxPaymentAttempts is < 1 or > 20)
        {
            failures.Add("Ticketing:Orders:MaxPaymentAttempts must be between 1 and 20.");
        }

        if (options.MaxOpenOrdersPerCustomer is < 1 or > 10)
        {
            failures.Add("Ticketing:Orders:MaxOpenOrdersPerCustomer must be between 1 and 10.");
        }

        if (options.PendingPaymentGraceMinutes is < 0 or > 60)
        {
            failures.Add("Ticketing:Orders:PendingPaymentGraceMinutes must be between 0 and 60.");
        }

        if (options.ReleaseBatchSize is < 1 or > 5000)
        {
            failures.Add("Ticketing:Orders:ReleaseBatchSize must be between 1 and 5000.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
