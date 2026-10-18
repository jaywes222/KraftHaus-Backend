namespace Payments.Domain.Payments;

/// <summary>
///     Payment state machine (Spec 08 §7.2, adapted to PalPluss):
///     <c>Initiating → Pending → Succeeded | Failed | Cancelled | Expired</c>; <c>Initiating → NotSent</c> when the
///     provider refuses the request. A provider success that arrives for an <c>Expired</c> payment still moves it to
///     <c>Succeeded</c> (late money is real money). A success whose amount does not match goes to <c>NeedsReview</c>.
/// </summary>
public enum PaymentStatus
{
    Initiating = 1,
    Pending = 2,
    Succeeded = 3,
    Failed = 4,
    Cancelled = 5,
    Expired = 6,
    NotSent = 7,
    NeedsReview = 8,

    /// <summary>Finance reviewed a <see cref="NeedsReview" /> payment and returned the money outside the system.</summary>
    Refunded = 9
}

/// <summary>Finance's decision on a payment whose amount did not match.</summary>
public enum PaymentReviewDecision
{
    /// <summary>Keep the money and fulfil the order as if the amount had matched.</summary>
    AcceptAsPaid = 1,

    /// <summary>The money was returned to the customer (M-Pesa reversal or manual refund).</summary>
    Refunded = 2
}

/// <summary>What the provider currently says about a transaction (PalPluss statuses, normalised).</summary>
public enum ProviderTransactionStatus
{
    Pending = 1,
    Processing = 2,
    Success = 3,
    Failed = 4,
    Cancelled = 5,
    Expired = 6,
    Reversed = 7
}
