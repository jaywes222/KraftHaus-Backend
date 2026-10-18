using System.Globalization;
using Common.Application.Auditing;
using Common.Domain.Aggregates;
using Common.Domain.Events;
using Ticketing.Domain.Orders;
using Ticketing.Domain.Tickets;
using Ticketing.Domain.Waitlist;

namespace Ticketing.Infrastructure.Auditing;

/// <summary>
///     Ticketing domain events → audit actions (<c>ticketing.waitlist.*</c>, <c>ticketing.order.*</c>,
///     <c>ticketing.ticket.*</c>). Metadata never carries phone numbers, names or e-mails.
/// </summary>
public static class TicketingAuditMapper
{
    public const string Module = "ticketing";

    public static (string Action, AuditTarget Target, IReadOnlyDictionary<string, string> Metadata) Map(
        DomainEvent domainEvent,
        IAggregateRoot aggregate)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        ArgumentNullException.ThrowIfNull(aggregate);

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);

        var (action, targetType) = domainEvent switch
        {
            WaitlistJoined joined => (Joined(joined), "waitlist_entry"),
            WaitlistConfirmationSent sent => (With("ticketing.waitlist.confirmation_sent", "eventId", Id(sent.EventId)), "waitlist_entry"),
            OrderPlaced placed => (Placed(placed), "order"),
            OrderPaymentStarted started => (With("ticketing.order.payment_started", "paymentId", Id(started.PaymentId)), "order"),
            OrderPaymentFailed failed => (Failed(failed), "order"),
            OrderPaid paid => (Paid(paid), "order"),
            OrderExpired => ("ticketing.order.expired", "order"),
            OrderDepositPaid deposit => (Deposit(deposit), "order"),
            OrderAttentionResolved => ("ticketing.order.attention_resolved", "order"),
            OrderCancelled cancelled => (With("ticketing.order.cancelled", "reason", cancelled.Reason), "order"),
            TicketAttendeeAssigned assigned => (With("ticketing.ticket.attendee_assigned", "assigned", assigned.Assigned ? "yes" : "no"), "ticket"),
            OrderNeedsAttention attention => (Attention(attention), "order"),
            TicketIssued issued => (Issued(issued), "ticket"),
            TicketCheckedIn checkedIn => (CheckedIn(checkedIn), "ticket"),
            TicketActivated => ("ticketing.ticket.activated", "ticket"),
            TicketVoided voided => (With("ticketing.ticket.voided", "reason", voided.Reason), "ticket"),
            TicketTransferred transferred => (With("ticketing.ticket.transferred", "transferNumber", transferred.TransferNumber.ToString(CultureInfo.InvariantCulture)), "ticket"),
            _ => throw new ArgumentOutOfRangeException(
                nameof(domainEvent),
                domainEvent.GetType().Name,
                "Every Ticketing domain event needs a stable audit action.")
        };

        return (action, new AuditTarget(targetType, aggregate.Id.Value.ToString("D", CultureInfo.InvariantCulture)), metadata);

        string Joined(WaitlistJoined joined)
        {
            metadata["eventId"] = Id(joined.EventId);
            metadata["marketingConsent"] = joined.MarketingConsent ? "on" : "off";
            return "ticketing.waitlist.joined";
        }

        string Placed(OrderPlaced placed)
        {
            metadata["eventId"] = Id(placed.EventId);
            metadata["admissions"] = placed.Admissions.ToString(CultureInfo.InvariantCulture);
            metadata["total"] = placed.Total.ToString(CultureInfo.InvariantCulture);
            return "ticketing.order.placed";
        }

        string Failed(OrderPaymentFailed failed)
        {
            metadata["paymentId"] = Id(failed.PaymentId);
            metadata["reason"] = failed.Reason;
            return "ticketing.order.payment_failed";
        }

        string Paid(OrderPaid paid)
        {
            metadata["paymentId"] = Id(paid.PaymentId);
            metadata["late"] = paid.Late ? "yes" : "no";
            return "ticketing.order.paid";
        }

        string Deposit(OrderDepositPaid deposit)
        {
            metadata["paymentId"] = Id(deposit.PaymentId);
            metadata["amount"] = deposit.Amount.ToString(CultureInfo.InvariantCulture);
            metadata["late"] = deposit.Late ? "yes" : "no";
            return "ticketing.order.deposit_paid";
        }

        string Attention(OrderNeedsAttention attention)
        {
            metadata["paymentId"] = Id(attention.PaymentId);
            metadata["reason"] = attention.Reason;
            return "ticketing.order.needs_attention";
        }

        string Issued(TicketIssued issued)
        {
            metadata["orderId"] = Id(issued.OrderId.Value);
            metadata["eventDateId"] = Id(issued.EventDateId);
            metadata["status"] = issued.Status.ToString();
            return "ticketing.ticket.issued";
        }

        string CheckedIn(TicketCheckedIn checkedIn)
        {
            metadata["eventDateId"] = Id(checkedIn.EventDateId);
            metadata["method"] = checkedIn.Method.ToString();
            return "ticketing.ticket.checked_in";
        }

        string With(string name, string key, string value)
        {
            metadata[key] = value;
            return name;
        }
    }

    private static string Id(Guid value) => value.ToString("D", CultureInfo.InvariantCulture);
}
