using Common.Domain.ResultMonad;
using Events.Domain.Common;

namespace Events.Domain.Model;

/// <summary>
///     Per-event switches other modules read: refunds (D-01, dormant by default), transfers (UF-006), Lipa Mdogo
///     Mdogo (UF-005), how long a checkout holds capacity (§8.1) and the order size cap.
/// </summary>
public sealed record EventPolicies
{
    public const int MinHoldMinutes = 2;
    public const int MaxHoldMinutes = 60;
    public const int DefaultHoldMinutes = 10;
    public const int MaxTransfersLimit = 10;
    public const int MaxTicketsPerOrderLimit = 50;
    public const int DefaultMaxTicketsPerOrder = 10;

    private EventPolicies()
    {
    }

    public bool RefundsEnabled { get; private init; }

    public bool TransfersEnabled { get; private init; }

    /// <summary>How many times one ticket may be transferred. 0 when transfers are disabled.</summary>
    public int MaxTransfersPerTicket { get; private init; }

    public bool LipaMdogoMdogoEnabled { get; private init; }

    public int HoldMinutes { get; private init; }

    public int MaxTicketsPerOrder { get; private init; }

    /// <summary>
    ///     Refunds off, one transfer allowed, Lipa Mdogo Mdogo off, 10-minute holds, 10 tickets per order. A new
    ///     instance each time: EF Core tracks owned values per owner, so two events must never share one.
    /// </summary>
    public static EventPolicies Default => new()
    {
        RefundsEnabled = false,
        TransfersEnabled = true,
        MaxTransfersPerTicket = 1,
        LipaMdogoMdogoEnabled = false,
        HoldMinutes = DefaultHoldMinutes,
        MaxTicketsPerOrder = DefaultMaxTicketsPerOrder
    };

    public static Result<EventPolicies> Create(
        bool refundsEnabled,
        bool transfersEnabled,
        int maxTransfersPerTicket,
        bool lipaMdogoMdogoEnabled,
        int holdMinutes,
        int maxTicketsPerOrder)
    {
        if (holdMinutes is < MinHoldMinutes or > MaxHoldMinutes)
        {
            return EventsErrors.Invalid(
                "HoldMinutes",
                $"Hold minutes must be between {MinHoldMinutes} and {MaxHoldMinutes}.");
        }

        if (maxTicketsPerOrder is < 1 or > MaxTicketsPerOrderLimit)
        {
            return EventsErrors.Invalid(
                "MaxTicketsPerOrder",
                $"Max tickets per order must be between 1 and {MaxTicketsPerOrderLimit}.");
        }

        if (transfersEnabled && maxTransfersPerTicket is < 1 or > MaxTransfersLimit)
        {
            return EventsErrors.Invalid(
                "MaxTransfersPerTicket",
                $"With transfers enabled, transfers per ticket must be between 1 and {MaxTransfersLimit}.");
        }

        return new EventPolicies
        {
            RefundsEnabled = refundsEnabled,
            TransfersEnabled = transfersEnabled,
            MaxTransfersPerTicket = transfersEnabled ? maxTransfersPerTicket : 0,
            LipaMdogoMdogoEnabled = lipaMdogoMdogoEnabled,
            HoldMinutes = holdMinutes,
            MaxTicketsPerOrder = maxTicketsPerOrder
        };
    }
}
