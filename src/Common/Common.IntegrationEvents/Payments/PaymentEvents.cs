namespace Common.IntegrationEvents.Payments;

/// <summary>What a payment is for. The owning module looks <c>ReferenceId</c> up in its own tables.</summary>
public enum PaymentPurpose
{
    /// <summary>Full payment of a ticket order (Ticketing order id).</summary>
    TicketOrder = 1,

    /// <summary>One installment of a Lipa Mdogo Mdogo plan (Ticketing installment id).</summary>
    TicketInstallment = 2,

    /// <summary>A small test payment proving a Kraft till receives money (Payments destination id, UF-017).</summary>
    TillTest = 3,

    /// <summary>A food or drink order paid to a Kraft's till (Commerce order id, UF-007/008).</summary>
    KraftOrder = 4
}

/// <summary>Why a payment did not complete.</summary>
public enum PaymentFailureReason
{
    /// <summary>M-Pesa declined it (insufficient funds, limits, wrong PIN, ...).</summary>
    Declined = 1,

    /// <summary>The customer dismissed the prompt.</summary>
    Cancelled = 2,

    /// <summary>No final answer within the configured time.</summary>
    Expired = 3,

    /// <summary>The provider refused to send the prompt (busy, balance, configuration).</summary>
    NotSent = 4,

    /// <summary>Money arrived but did not match; Finance returned it.</summary>
    Refunded = 5
}

/// <summary>
///     The provider confirmed the money (Spec 08 §7.2), checked against the provider's own record, not the callback
///     body. <paramref name="Late" /> is true when it arrived after the payment had already been given up on.
/// </summary>
public sealed record PaymentSucceeded(
    Guid PaymentId,
    PaymentPurpose Purpose,
    Guid ReferenceId,
    decimal Amount,
    string Currency,
    string? MpesaReceipt,
    DateTimeOffset PaidAt,
    bool Late) : IntegrationEvent;

public sealed record PaymentFailed(
    Guid PaymentId,
    PaymentPurpose Purpose,
    Guid ReferenceId,
    PaymentFailureReason Reason,
    string? ResultDescription) : IntegrationEvent;
