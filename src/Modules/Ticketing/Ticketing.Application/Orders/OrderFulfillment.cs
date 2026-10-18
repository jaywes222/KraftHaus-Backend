using Common.Application.EventCatalog;
using Common.Application.Messaging;
using Common.Domain.ResultMonad;
using Common.IntegrationEvents.Payments;
using Microsoft.Extensions.Logging;
using Ticketing.Application.Abstractions;
using Ticketing.Application.Checkout;
using Ticketing.Application.Tickets;
using Ticketing.Domain.Common;
using Ticketing.Domain.Orders;
using Ticketing.Domain.Tickets;

namespace Ticketing.Application.Orders;

/// <summary>
///     Turns confirmed money into tickets (Spec 08 §8.1): in one transaction the order is marked paid, its held
///     capacity becomes sold and one ticket per admission is issued, together with the processed-message row so a
///     re-delivered event changes nothing.
/// </summary>
public sealed partial class OrderPaymentSucceededHandler(
    IOrderRepository orders,
    ITicketRepository tickets,
    ICapacityLedger capacity,
    IEventCatalog events,
    ITicketingUnitOfWork unitOfWork,
    ITicketNotifier notifier,
    TimeProvider time,
    ILogger<OrderPaymentSucceededHandler> logger)
    : IIntegrationEventHandler<PaymentSucceeded>
{
    public const string HandlerName = "ticketing.order-payment-succeeded";

    public async Task HandleAsync(PaymentSucceeded @event, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@event);

        if (@event.Purpose is not (PaymentPurpose.TicketOrder or PaymentPurpose.TicketInstallment) ||
            await unitOfWork.AlreadyProcessedAsync(@event.Id, HandlerName, cancellationToken))
        {
            return;
        }

        var order = await orders.FindAsync(new OrderId(@event.ReferenceId), cancellationToken)
                    ?? throw new InvalidOperationException($"Order {@event.ReferenceId} for payment {@event.PaymentId} does not exist.");

        if (@event.Purpose == PaymentPurpose.TicketInstallment)
        {
            await BalancePaidAsync(order, @event, cancellationToken);
            return;
        }

        var sale = await events.GetSaleSnapshotAsync(order.EventId, cancellationToken)
                   ?? throw new InvalidOperationException($"Event {order.EventId} of order {order.Id.Value} does not exist.");

        var issued = new List<Ticket>();

        var result = await unitOfWork.InTransactionAsync<bool>(
            async ct =>
            {
                unitOfWork.MarkProcessed(@event.Id, HandlerName);

                if (order.Status == OrderStatus.Paid)
                {
                    // A second prompt was also paid. The order keeps its tickets; Finance refunds the extra money.
                    order.FlagUnusablePayment(@event.PaymentId, "Second payment for an order that is already paid.");
                    return await SaveAsync(ct);
                }

                if (order.Status is not (OrderStatus.AwaitingPayment or OrderStatus.Expired))
                {
                    order.FlagUnusablePayment(@event.PaymentId, $"Payment arrived for an order that is {order.Status}.");
                    return await SaveAsync(ct);
                }

                var seats = await capacity.ConvertAsync(order.Id, ct)
                    ? Result.Success
                    : await capacity.ReacquireAsSoldAsync(order.Id, ct);

                if (seats.IsFailure)
                {
                    // Rolled back below; recorded in a fresh transaction so the money is not lost from view.
                    return seats.Error!;
                }

                var paid = order.MarkPaid(@event.PaymentId, time.GetUtcNow());
                if (paid.IsFailure)
                {
                    return paid.Error!;
                }

                var status = order.PaymentPlan == PaymentPlan.LipaMdogoMdogo ? TicketStatus.AwaitingBalance : TicketStatus.Valid;
                issued.AddRange(TicketIssuer.Issue(order, sale, status, time.GetUtcNow()));
                tickets.AddRange(issued);

                return await SaveAsync(ct);
            },
            cancellationToken);

        if (result.IsFailure)
        {
            if (result.Error!.Key != "Ticketing.SoldOut")
            {
                // A concurrent update (or anything else): let the outbox deliver it again.
                throw new HandlerRetryException(result.Error);
            }

            await RecordSeatsGoneAsync(order.Id, @event, cancellationToken);
            return;
        }

        if (issued.Count > 0)
        {
            await notifier.TicketsIssuedAsync(order, sale.Title, issued.Count, CancellationToken.None);
        }
    }

    /// <summary>Lipa Mdogo Mdogo balance: the order becomes paid and its tickets get their QR codes.</summary>
    private async Task BalancePaidAsync(Order order, PaymentSucceeded @event, CancellationToken cancellationToken)
    {
        var activated = false;

        var result = await unitOfWork.InTransactionAsync<bool>(
            async ct =>
            {
                unitOfWork.MarkProcessed(@event.Id, HandlerName);

                if (order.MarkBalancePaid(@event.PaymentId, time.GetUtcNow()).IsFailure)
                {
                    // Cancelled for non-payment (or already paid): BR-011 routes the money to Finance.
                    order.FlagUnusablePayment(@event.PaymentId, $"Balance arrived for an order that is {order.Status}.");
                    return await SaveAsync(ct);
                }

                foreach (var ticket in await tickets.ListForOrderAsync(order.Id, ct))
                {
                    ticket.Activate();
                }

                activated = true;
                return await SaveAsync(ct);
            },
            cancellationToken);

        HandlerRetryException.ThrowIfFailed(result);

        if (activated)
        {
            var sale = await events.GetSaleSnapshotAsync(order.EventId, cancellationToken);
            await notifier.BalancePaidAsync(order, sale?.Title ?? "your event", CancellationToken.None);
        }
    }

    private async Task<Result<bool>> SaveAsync(CancellationToken cancellationToken)
    {
        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error!;
        }

        return true;
    }

    private async Task RecordSeatsGoneAsync(OrderId orderId, PaymentSucceeded @event, CancellationToken cancellationToken)
    {
        LogSeatsGone(logger, orderId.Value, @event.PaymentId);

        // The failed transaction's changes were detached; reload and record the outcome on its own.
        var order = await orders.FindAsync(orderId, cancellationToken)
                    ?? throw new InvalidOperationException($"Order {orderId.Value} disappeared.");

        var recorded = await unitOfWork.InTransactionAsync<bool>(
            async ct =>
            {
                unitOfWork.MarkProcessed(@event.Id, HandlerName);
                order.FlagUnusablePayment(@event.PaymentId, "Paid after the hold ran out and the seats were sold to someone else.");
                return await SaveAsync(ct);
            },
            cancellationToken);

        HandlerRetryException.ThrowIfFailed(recorded);
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "Late payment {PaymentId} for order {OrderId} could not get its seats back; flagged for Finance.")]
    private static partial void LogSeatsGone(ILogger logger, Guid orderId, Guid paymentId);
}

/// <summary>Records a failed prompt on the order; the customer may try again while the hold lasts.</summary>
public sealed class OrderPaymentFailedHandler(
    IOrderRepository orders,
    ITicketingUnitOfWork unitOfWork)
    : IIntegrationEventHandler<PaymentFailed>
{
    public const string HandlerName = "ticketing.order-payment-failed";

    public async Task HandleAsync(PaymentFailed @event, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@event);

        if (@event.Purpose is not (PaymentPurpose.TicketOrder or PaymentPurpose.TicketInstallment) ||
            await unitOfWork.AlreadyProcessedAsync(@event.Id, HandlerName, cancellationToken))
        {
            return;
        }

        var order = await orders.FindAsync(new OrderId(@event.ReferenceId), cancellationToken);

        unitOfWork.MarkProcessed(@event.Id, HandlerName);
        order?.RecordPaymentFailed(@event.PaymentId, @event.Reason.ToString());

        HandlerRetryException.ThrowIfFailed(await unitOfWork.SaveChangesAsync(cancellationToken));
    }
}
