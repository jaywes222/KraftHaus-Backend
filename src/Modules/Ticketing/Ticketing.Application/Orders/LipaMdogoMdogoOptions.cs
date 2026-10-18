using Microsoft.Extensions.Options;

namespace Ticketing.Application.Orders;

/// <summary>
///     Lipa Mdogo Mdogo terms (<c>Ticketing:LipaMdogoMdogo</c>). Whether an event offers it is the event's policy
///     (Events); these are the platform terms. O-03 resolved for Event 001: deposit on the whole order.
/// </summary>
public sealed class LipaMdogoMdogoOptions
{
    public const string SectionName = "Ticketing:LipaMdogoMdogo";

    public int DepositPercent { get; init; } = 50;

    /// <summary>The balance is due this many days before the order's first event date.</summary>
    public int BalanceDueDaysBeforeEvent { get; init; } = 7;

    /// <summary>Installments are only offered while at least this many days remain before the balance is due.</summary>
    public int MinDaysBeforeDue { get; init; } = 2;

    /// <summary>Reminder SMS this many days before the due date (one per entry).</summary>
    public IReadOnlyList<int> ReminderDaysBeforeDue { get; init; } = [3, 1];

    /// <summary>Hours after the due date before an unpaid order is cancelled.</summary>
    public int OverdueGraceHours { get; init; } = 24;

    public string RemindersCron { get; init; } = "7 * * * *";

    public int BatchSize { get; init; } = 200;
}

public sealed class LipaMdogoMdogoOptionsValidator : IValidateOptions<LipaMdogoMdogoOptions>
{
    public ValidateOptionsResult Validate(string? name, LipaMdogoMdogoOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.DepositPercent is < 10 or > 90)
        {
            failures.Add("Ticketing:LipaMdogoMdogo:DepositPercent must be between 10 and 90.");
        }

        if (options.BalanceDueDaysBeforeEvent is < 0 or > 90 || options.MinDaysBeforeDue is < 0 or > 60)
        {
            failures.Add("Ticketing:LipaMdogoMdogo:BalanceDueDaysBeforeEvent (0-90) or MinDaysBeforeDue (0-60) is out of range.");
        }

        if (options.ReminderDaysBeforeDue.Any(x => x is < 0 or > 60))
        {
            failures.Add("Ticketing:LipaMdogoMdogo:ReminderDaysBeforeDue entries must be between 0 and 60.");
        }

        if (options.OverdueGraceHours is < 0 or > 168 || options.BatchSize is < 1 or > 5000)
        {
            failures.Add("Ticketing:LipaMdogoMdogo:OverdueGraceHours (0-168) or BatchSize (1-5000) is out of range.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
