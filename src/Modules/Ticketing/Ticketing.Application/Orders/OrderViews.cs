using Common.Application.Payments;
using Ticketing.Domain.Orders;

namespace Ticketing.Application.Orders;

/// <summary>Body of <c>POST /api/client/orders</c>.</summary>
/// <param name="PayerPhone">Phone that gets the M-Pesa prompt; defaults to the account's phone.</param>
/// <param name="PaymentPlan"><c>Full</c> (default) or <c>LipaMdogoMdogo</c> (deposit now, balance before the event).</param>
public sealed record PlaceOrderRequest(
    string? EventSlug,
    IReadOnlyList<PlaceOrderItem>? Items,
    string? PayerPhone,
    PaymentPlan PaymentPlan = PaymentPlan.Full);

public sealed record PlaceOrderItem(Guid? TierId, Guid? OfferId, int Quantity);

/// <summary>Body of <c>POST /api/client/orders/{id}/payments</c> (retry the prompt, optionally on another phone).</summary>
public sealed record RetryPaymentRequest(string? PayerPhone);

public sealed record OrderLineView(
    string Name,
    string Kind,
    int Units,
    int Admissions,
    decimal UnitPrice,
    decimal LineTotal);

/// <param name="Status">Pending, Succeeded, Failed or NeedsReview.</param>
/// <param name="ErrorKey">Why the prompt could not be sent, e.g. <c>Payments.ProviderBusy</c>.</param>
public sealed record OrderPaymentView(
    Guid PaymentId,
    string Status,
    string? FailureReason,
    string? ErrorKey,
    int? RetryAfterSeconds,
    string? MpesaReceipt);

/// <param name="Status">
///     AwaitingPayment, Paid, Expired (also when the hold ran out but the release job has not run yet),
///     NeedsAttention, PartiallyPaid.
/// </param>
public sealed record OrderView(
    Guid Id,
    string Reference,
    Guid EventId,
    string Status,
    decimal Total,
    string Currency,
    int Admissions,
    DateTimeOffset PlacedAt,
    DateTimeOffset HoldExpiresAt,
    int PaymentAttempts,
    string? LastPaymentFailure,
    IReadOnlyList<OrderLineView> Lines,
    OrderPaymentView? Payment,
    int TicketsIssued,
    string PaymentPlan,
    decimal AmountPaid,
    decimal AmountDueNow,
    DateTimeOffset? BalanceDueAt,
    decimal Subtotal,
    decimal PlatformFee,
    bool FeeWaived)
{
    public static OrderView From(Order order, MobilePaymentView? payment, int ticketsIssued, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(order);

        return new OrderView(
            order.Id.Value,
            order.Reference,
            order.EventId,
            order.IsHoldExpired(now) ? nameof(OrderStatus.Expired) : order.Status.ToString(),
            order.Total,
            "KES",
            order.Admissions,
            order.PlacedAt,
            order.HoldExpiresAt,
            order.PaymentAttempts,
            order.LastPaymentFailure,
            [.. order.Lines.Select(x => new OrderLineView(x.Name, x.Kind.ToString(), x.Units, x.Admissions, x.UnitPrice, x.LineTotal))],
            payment is null
                ? null
                : new OrderPaymentView(
                    payment.PaymentId,
                    payment.Status.ToString(),
                    payment.FailureReason?.ToString(),
                    payment.ErrorKey,
                    payment.RetryAfterSeconds,
                    payment.MpesaReceipt),
            ticketsIssued,
            order.PaymentPlan.ToString(),
            order.AmountPaid,
            order.Status is OrderStatus.AwaitingPayment or OrderStatus.PartiallyPaid ? order.AmountDueNow : 0m,
            order.BalanceDueAt,
            order.Subtotal,
            order.PlatformFee,
            order.FeeWaived);
    }
}
