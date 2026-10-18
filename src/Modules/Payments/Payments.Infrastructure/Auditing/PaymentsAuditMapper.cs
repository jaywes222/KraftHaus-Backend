using System.Globalization;
using Common.Application.Auditing;
using Common.Domain.Aggregates;
using Common.Domain.Events;
using Payments.Domain.Destinations;
using Payments.Domain.Payments;

namespace Payments.Infrastructure.Auditing;

/// <summary>
///     Payment domain events → audit actions (<c>payments.payment.*</c>). Metadata carries purpose, reference, amount
///     and provider codes; never the phone number.
/// </summary>
public static class PaymentsAuditMapper
{
    public const string Module = "payments";

    public static (string Action, AuditTarget Target, IReadOnlyDictionary<string, string> Metadata) Map(
        DomainEvent domainEvent,
        IAggregateRoot aggregate)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        ArgumentNullException.ThrowIfNull(aggregate);

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);

        var action = domainEvent switch
        {
            PaymentCreated created => Created(created),
            PaymentPromptSent => "payments.payment.prompt_sent",
            PaymentPromptNotSent notSent => With("payments.payment.prompt_not_sent", "providerError", notSent.ProviderErrorCode),
            PaymentConfirmed confirmed => With("payments.payment.succeeded", "late", confirmed.Late ? "yes" : "no"),
            PaymentNotCompleted notCompleted => NotCompleted(notCompleted),
            PaymentFlaggedForReview flagged => Flagged(flagged),
            PaymentReviewed reviewed => With("payments.payment.reviewed", "decision", reviewed.Decision.ToString()),
            SettlementRecorded settlement => With("payments.payment.settlement_recorded", "status", settlement.Status.ToString()),
            TillSubmitted submitted => Till("payments.till.submitted", submitted.KraftId, submitted.EventId),
            TillTestStarted testStarted => With("payments.till.test_started", "paymentId", testStarted.PaymentId.ToString("D", CultureInfo.InvariantCulture)),
            TillTestFinished testFinished => With("payments.till.test_finished", "passed", testFinished.Passed ? "yes" : "no"),
            TillApproved approved => Till("payments.till.approved", approved.KraftId, approved.EventId),
            TillRejected => "payments.till.rejected",
            TillDisabled => "payments.till.disabled",
            _ => throw new ArgumentOutOfRangeException(nameof(domainEvent), domainEvent.GetType().Name, "Unmapped payment event.")
        };

        var targetType = domainEvent is TillSubmitted or TillTestStarted or TillTestFinished or TillApproved or TillRejected or TillDisabled
            ? "till"
            : "payment";

        return (action, new AuditTarget(targetType, aggregate.Id.Value.ToString("D", CultureInfo.InvariantCulture)), metadata);

        string Till(string name, Guid kraftId, Guid eventId)
        {
            metadata["kraftId"] = kraftId.ToString("D", CultureInfo.InvariantCulture);
            metadata["eventId"] = eventId.ToString("D", CultureInfo.InvariantCulture);
            return name;
        }

        string Created(PaymentCreated created)
        {
            metadata["purpose"] = created.Purpose.ToString();
            metadata["referenceId"] = created.ReferenceId.ToString("D", CultureInfo.InvariantCulture);
            metadata["amount"] = created.Amount.ToString(CultureInfo.InvariantCulture);
            return "payments.payment.created";
        }

        string NotCompleted(PaymentNotCompleted notCompleted)
        {
            metadata["status"] = notCompleted.Status.ToString();
            if (notCompleted.ResultCode is { } code)
            {
                metadata["resultCode"] = code;
            }

            return "payments.payment.not_completed";
        }

        string Flagged(PaymentFlaggedForReview flagged)
        {
            metadata["expected"] = flagged.ExpectedAmount.ToString(CultureInfo.InvariantCulture);
            metadata["reported"] = flagged.ReportedAmount.ToString(CultureInfo.InvariantCulture);
            return "payments.payment.flagged_for_review";
        }

        string With(string name, string key, string? value)
        {
            if (value is not null)
            {
                metadata[key] = value;
            }

            return name;
        }
    }
}
