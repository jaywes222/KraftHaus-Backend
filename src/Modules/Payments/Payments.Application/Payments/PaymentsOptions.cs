using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Payments.Application.Payments;

/// <summary>Operator-tunable payment timings (<c>Payments</c>). PalPluss credentials live in <c>Payments:PalPluss</c>.</summary>
public sealed class PaymentsOptions
{
    public const string SectionName = "Payments";

    /// <summary>Scheme of the callback URL. Only Development may use plain http (PalPluss itself requires https).</summary>
    public string CallbackScheme { get; init; } = "https";

    /// <summary>
    ///     Host the provider calls back on instead of the tenant host of the request (a tunnel in development). It must
    ///     resolve to the same tenant.
    /// </summary>
    public string? CallbackHostOverride { get; init; }

    /// <summary>First status check after the prompt was sent (PalPluss: about 15 s).</summary>
    public int FirstCheckAfterSeconds { get; init; } = 15;

    /// <summary>Minimum time between status checks of one payment.</summary>
    public int CheckIntervalSeconds { get; init; } = 20;

    /// <summary>Upper bound on provider lookups per tenant per job run (PalPluss allows 60 requests/minute per key).</summary>
    public int MaxChecksPerRun { get; init; } = 15;

    /// <summary>Stop waiting for a final answer after this long and report the payment as not completed.</summary>
    public int PendingTimeoutMinutes { get; init; } = 5;

    /// <summary>Keep checking expired payments for a late success for this long.</summary>
    public int LateSuccessWindowMinutes { get; init; } = 60;

    /// <summary>Cron for the status-check job (UTC).</summary>
    public string CheckPaymentsCron { get; init; } = "* * * * *";

    /// <summary>Whole shillings sent through a new till to prove it receives money (UF-017).</summary>
    public decimal TillTestAmount { get; init; } = 1m;
}

public sealed class PaymentsOptionsValidator(IHostEnvironment environment) : IValidateOptions<PaymentsOptions>
{
    public ValidateOptionsResult Validate(string? name, PaymentsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.CallbackScheme is not ("https" or "http"))
        {
            failures.Add("Payments:CallbackScheme must be https or http.");
        }

        if (options.CallbackScheme == "http" && !environment.IsDevelopment())
        {
            failures.Add("Payments:CallbackScheme may only be http in Development.");
        }

        if (options.FirstCheckAfterSeconds is < 5 or > 300 || options.CheckIntervalSeconds is < 5 or > 600)
        {
            failures.Add("Payments:FirstCheckAfterSeconds and CheckIntervalSeconds must be between 5 and 300/600.");
        }

        if (options.MaxChecksPerRun is < 1 or > 55)
        {
            failures.Add("Payments:MaxChecksPerRun must be between 1 and 55 (PalPluss allows 60 requests/minute).");
        }

        if (options.PendingTimeoutMinutes is < 2 or > 60 || options.LateSuccessWindowMinutes < options.PendingTimeoutMinutes)
        {
            failures.Add("Payments:PendingTimeoutMinutes must be 2-60 and not above LateSuccessWindowMinutes.");
        }

        if (options.TillTestAmount is < 1m or > 100m || decimal.Round(options.TillTestAmount, 0) != options.TillTestAmount)
        {
            failures.Add("Payments:TillTestAmount must be whole shillings between 1 and 100.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
