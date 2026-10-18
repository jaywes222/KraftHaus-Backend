namespace Ticketing.Domain.Orders;

/// <summary>
///     <c>AwaitingPayment → Paid</c>; <c>AwaitingPayment → Expired</c> when the hold runs out; <c>Expired → Paid</c>
///     when money arrives late and the seats are still there, otherwise <c>Expired → NeedsAttention</c> (Finance
///     refunds it). <c>AwaitingPayment → PartiallyPaid → Paid</c> for Lipa Mdogo Mdogo (stage 3).
/// </summary>
public enum OrderStatus
{
    AwaitingPayment = 1,
    Paid = 2,
    Expired = 3,
    NeedsAttention = 4,
    PartiallyPaid = 5,
    Cancelled = 6
}

public enum OrderItemKind
{
    /// <summary>Single admissions at the tier's current phase price.</summary>
    Admission = 1,

    /// <summary>A Couple / Gang bundle: one price, several admissions.</summary>
    Bundle = 2
}
