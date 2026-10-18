using System.Security.Cryptography;
using System.Text;
using Common.Domain.Aggregates;
using Common.Domain.ResultMonad;
using Common.IntegrationEvents.Payments;
using Payments.Domain.Common;

namespace Payments.Domain.Payments;

/// <summary>
///     One M-Pesa STK push through PalPluss for one purpose (a ticket order, an installment). The provider's word is
///     only taken from its own transaction record (<c>GET /transactions/{id}</c>): callbacks are unsigned, so a callback
///     only tells us when to look.
/// </summary>
public sealed class Payment : AggregateRoot<PaymentId>
{
    public const string ProviderName = "PalPluss";
    public const int AccountReferenceMaxLength = 12;
    public const int DescriptionMaxLength = 13;
    public const int ProviderIdMaxLength = 64;
    public const int ResultDescriptionMaxLength = 500;
    public const decimal MinAmount = 1m;
    public const decimal MaxAmount = 250_000m;

    private Payment(PaymentId id)
        : base(id)
    {
    }

    public PaymentPurpose Purpose { get; private set; }

    /// <summary>The owning module's id for <see cref="Purpose" /> (order id, installment id).</summary>
    public Guid ReferenceId { get; private set; }

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = "KES";

    /// <summary>E.164 phone that receives the prompt.</summary>
    public string Phone { get; private set; } = string.Empty;

    public string AccountReference { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public string Provider { get; private set; } = ProviderName;

    /// <summary>SHA-256 (hex) of the per-payment callback token in the callback URL. The token itself is never stored.</summary>
    public string CallbackTokenHash { get; private set; } = string.Empty;

    public PaymentStatus Status { get; private set; }

    public string? ProviderTransactionId { get; private set; }

    public string? ProviderCheckoutId { get; private set; }

    public string? MpesaReceipt { get; private set; }

    public string? ResultCode { get; private set; }

    public string? ResultDescription { get; private set; }

    /// <summary>Error code returned by the provider when it refused to send the prompt.</summary>
    public string? ProviderErrorCode { get; private set; }

    public decimal? ReportedAmount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? PromptSentAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public DateTimeOffset? LastCheckedAt { get; private set; }

    public int CheckCount { get; private set; }

    /// <summary>The till the money goes to; null for the platform account (tickets).</summary>
    public Guid? DestinationId { get; private set; }

    /// <summary>The PalPluss channel the prompt was sent through (copied from the till when it was sent).</summary>
    public Guid? ProviderChannelId { get; private set; }

    public PaymentReviewDecision? ReviewDecision { get; private set; }

    public string? ReviewNote { get; private set; }

    public Guid? ReviewedBy { get; private set; }

    public DateTimeOffset? ReviewedAt { get; private set; }

    public bool IsFinal => Status is PaymentStatus.Succeeded or PaymentStatus.Failed or PaymentStatus.Cancelled
        or PaymentStatus.NotSent or PaymentStatus.NeedsReview or PaymentStatus.Refunded;

    public static Result<Payment> Create(
        PaymentId id,
        PaymentPurpose purpose,
        Guid referenceId,
        decimal amount,
        string phone,
        string accountReference,
        string description,
        string callbackToken,
        DateTimeOffset now,
        Guid? destinationId = null,
        Guid? providerChannelId = null)
    {
        if (!Enum.IsDefined(purpose))
        {
            return PaymentsErrors.Invalid("Purpose", "Unknown payment purpose.");
        }

        if (referenceId == Guid.Empty)
        {
            return PaymentsErrors.Invalid("ReferenceId", "A payment must reference what it pays for.");
        }

        if (amount is < MinAmount or > MaxAmount || decimal.Round(amount, 0) != amount)
        {
            return PaymentsErrors.Invalid("Amount", $"M-Pesa amounts are whole shillings between {MinAmount} and {MaxAmount}.");
        }

        if (string.IsNullOrWhiteSpace(phone) || phone.Length != 13 || !phone.StartsWith("+254", StringComparison.Ordinal))
        {
            return PaymentsErrors.Invalid("Phone", "The phone must be an E.164 Kenyan mobile number.");
        }

        if (string.IsNullOrWhiteSpace(accountReference) || accountReference.Length > AccountReferenceMaxLength)
        {
            return PaymentsErrors.Invalid("AccountReference", $"The account reference must be 1-{AccountReferenceMaxLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(description) || description.Length > DescriptionMaxLength)
        {
            return PaymentsErrors.Invalid("Description", $"The description must be 1-{DescriptionMaxLength} characters.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(callbackToken);

        var payment = new Payment(id)
        {
            Purpose = purpose,
            ReferenceId = referenceId,
            Amount = amount,
            Phone = phone,
            AccountReference = accountReference,
            Description = description,
            CallbackTokenHash = HashToken(callbackToken),
            Status = PaymentStatus.Initiating,
            CreatedAt = now,
            DestinationId = destinationId,
            ProviderChannelId = providerChannelId
        };

        payment.RaiseEvent(new PaymentCreated(id, purpose, referenceId, amount));

        return payment;
    }

    public static string HashToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>The provider accepted the request and sent the prompt.</summary>
    public void MarkPromptSent(string providerTransactionId, string? checkoutId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerTransactionId);

        if (Status != PaymentStatus.Initiating)
        {
            return;
        }

        var @event = new PaymentPromptSent(Id);

        ProviderTransactionId = Fit(providerTransactionId, ProviderIdMaxLength);
        ProviderCheckoutId = Fit(checkoutId, ProviderIdMaxLength);
        Status = PaymentStatus.Pending;
        PromptSentAt = now;

        RaiseEvent(@event);
    }

    /// <summary>The provider refused to send the prompt; nothing was charged.</summary>
    public void MarkNotSent(string? providerErrorCode, string? message, DateTimeOffset now)
    {
        if (Status != PaymentStatus.Initiating)
        {
            return;
        }

        var @event = new PaymentPromptNotSent(Id, providerErrorCode);

        Status = PaymentStatus.NotSent;
        ProviderErrorCode = Fit(providerErrorCode, ProviderIdMaxLength);
        ResultDescription = Fit(message, ResultDescriptionMaxLength);
        CompletedAt = now;

        RaiseEvent(@event);
    }

    /// <summary>
    ///     Adopts the provider transaction id learnt from a callback when the request crashed before saving it.
    ///     Only while still <see cref="PaymentStatus.Initiating" />.
    /// </summary>
    public void AdoptProviderTransaction(string providerTransactionId, DateTimeOffset now)
    {
        if (Status == PaymentStatus.Initiating && ProviderTransactionId is null)
        {
            MarkPromptSent(providerTransactionId, null, now);
        }
    }

    /// <summary>
    ///     Applies what the provider's own record says. Returns the transition so the caller can tell the owning
    ///     module (exactly once per transition).
    /// </summary>
    public PaymentTransition ApplyProviderRecord(ProviderRecord record, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(record);

        LastCheckedAt = now;
        CheckCount++;

        // Final states never move again, except our own "gave up waiting" (Expired) being overtaken by real money.
        if (IsFinal)
        {
            return PaymentTransition.None;
        }

        switch (record.Status)
        {
            case ProviderTransactionStatus.Success:
                var late = Status == PaymentStatus.Expired;
                MpesaReceipt = Fit(record.MpesaReceipt, ProviderIdMaxLength);
                ResultCode = Fit(record.ResultCode, ProviderIdMaxLength);
                ResultDescription = Fit(record.ResultDescription, ResultDescriptionMaxLength);
                ReportedAmount = record.Amount;
                CompletedAt = now;

                if (record.Amount != Amount)
                {
                    var flagged = new PaymentFlaggedForReview(Id, Amount, record.Amount);
                    Status = PaymentStatus.NeedsReview;
                    RaiseEvent(flagged);
                    return PaymentTransition.FlaggedForReview;
                }

                var confirmed = new PaymentConfirmed(Id, late);
                Status = PaymentStatus.Succeeded;
                RaiseEvent(confirmed);
                return late ? PaymentTransition.SucceededLate : PaymentTransition.Succeeded;

            case ProviderTransactionStatus.Failed or ProviderTransactionStatus.Reversed:
                return Finish(PaymentStatus.Failed, record, now);

            case ProviderTransactionStatus.Cancelled:
                return Finish(PaymentStatus.Cancelled, record, now);

            case ProviderTransactionStatus.Expired:
                return Status == PaymentStatus.Expired ? PaymentTransition.None : Finish(PaymentStatus.Expired, record, now);

            default:
                return PaymentTransition.None;
        }
    }

    /// <summary>
    ///     Gives up waiting: no final answer within the configured time. Not final: a later provider success still
    ///     turns it into <see cref="PaymentStatus.Succeeded" /> (late).
    /// </summary>
    public PaymentTransition Expire(DateTimeOffset now)
    {
        if (Status is not (PaymentStatus.Initiating or PaymentStatus.Pending))
        {
            return PaymentTransition.None;
        }

        var @event = new PaymentNotCompleted(Id, PaymentStatus.Expired, null);

        Status = PaymentStatus.Expired;
        CompletedAt = now;

        RaiseEvent(@event);

        return PaymentTransition.Failed;
    }

    /// <summary>
    ///     Finance resolves a <see cref="PaymentStatus.NeedsReview" /> payment: accept it (the order is fulfilled) or
    ///     record that the money was returned (the order can be paid again).
    /// </summary>
    public Result<PaymentTransition> Review(PaymentReviewDecision decision, string? note, Guid reviewerId, DateTimeOffset now)
    {
        if (Status != PaymentStatus.NeedsReview)
        {
            return PaymentsErrors.InvalidState("NotUnderReview", "Only a payment waiting for review can be reviewed.");
        }

        if (!Enum.IsDefined(decision))
        {
            return PaymentsErrors.Invalid("Decision", "Choose AcceptAsPaid or Refunded.");
        }

        var trimmed = note?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > ResultDescriptionMaxLength)
        {
            return PaymentsErrors.Invalid("Note", $"Explain the decision in 1-{ResultDescriptionMaxLength} characters.");
        }

        var @event = new PaymentReviewed(Id, decision);

        ReviewDecision = decision;
        ReviewNote = trimmed;
        ReviewedBy = reviewerId;
        ReviewedAt = now;
        Status = decision == PaymentReviewDecision.AcceptAsPaid ? PaymentStatus.Succeeded : PaymentStatus.Refunded;

        RaiseEvent(@event);

        return decision == PaymentReviewDecision.AcceptAsPaid ? PaymentTransition.Succeeded : PaymentTransition.Failed;
    }

    public PaymentFailureReason? FailureReason =>
        Status switch
        {
            PaymentStatus.Failed => PaymentFailureReason.Declined,
            PaymentStatus.Cancelled => PaymentFailureReason.Cancelled,
            PaymentStatus.Expired => PaymentFailureReason.Expired,
            PaymentStatus.NotSent => PaymentFailureReason.NotSent,
            PaymentStatus.Refunded => PaymentFailureReason.Refunded,
            _ => null
        };

    private PaymentTransition Finish(PaymentStatus status, ProviderRecord record, DateTimeOffset now)
    {
        // Already reported as not completed when we stopped waiting; record the real reason, don't report twice.
        var alreadyReported = Status == PaymentStatus.Expired;
        var @event = new PaymentNotCompleted(Id, status, record.ResultCode);

        Status = status;
        ResultCode = Fit(record.ResultCode, ProviderIdMaxLength);
        ResultDescription = Fit(record.ResultDescription, ResultDescriptionMaxLength);
        CompletedAt = now;

        RaiseEvent(@event);

        return alreadyReported ? PaymentTransition.None : PaymentTransition.Failed;
    }

    private static string? Fit(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}

/// <summary>The provider's own record of a transaction.</summary>
public sealed record ProviderRecord(
    string TransactionId,
    ProviderTransactionStatus Status,
    decimal Amount,
    string? AccountReference,
    string? MpesaReceipt,
    string? ResultCode,
    string? ResultDescription);

public enum PaymentTransition
{
    None = 0,
    Succeeded = 1,
    SucceededLate = 2,
    Failed = 3,
    FlaggedForReview = 4
}
