using Commerce.Application.Abstractions;
using Commerce.Application.Stock;
using Commerce.Domain.Common;
using Commerce.Domain.Orders;
using Common.Application.Messaging;
using Common.Domain.ResultMonad;
using Common.IntegrationEvents.Payments;
using Microsoft.Extensions.Logging;

namespace Commerce.Application.Orders;

/// <summary>
///     <c>PaymentSucceeded</c> for a Kraft order: in one transaction the held portions become sold and the order is
///     paid with its fee split fixed. Money that arrives after the hold ran out takes the portions back if they are still
///     free; otherwise the order waits for a Finance refund (refunds are manual, D-01).
/// </summary>
public sealed partial class KraftOrderPaymentSucceededHandler(
    IKraftOrderRepository orders,
    IStockLedger stock,
    ICommerceUnitOfWork unitOfWork,
    IKraftOrderNotifier notifier,
    IKraftOrderRealtime realtime,
    TimeProvider time,
    ILogger<KraftOrderPaymentSucceededHandler> logger)
    : IIntegrationEventHandler<PaymentSucceeded>
{
    public const string HandlerName = "commerce.order-payment-succeeded";

    public async Task HandleAsync(PaymentSucceeded @event, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@event);

        if (@event.Purpose != PaymentPurpose.KraftOrder ||
            await unitOfWork.AlreadyProcessedAsync(@event.Id, HandlerName, cancellationToken))
        {
            return;
        }

        var order = await orders.FindAsync(new KraftOrderId(@event.ReferenceId), cancellationToken)
                    ?? throw new InvalidOperationException($"Kraft order {@event.ReferenceId} for payment {@event.PaymentId} does not exist.");

        var paidNow = false;

        var result = await unitOfWork.InTransactionAsync<bool>(
            async ct =>
            {
                unitOfWork.MarkProcessed(@event.Id, HandlerName);

                if (order.IsPaid)
                {
                    order.FlagUnusablePayment(@event.PaymentId, @event.Amount, "A second payment arrived for an order that is already paid.");
                    return await SaveAsync(ct);
                }

                var portions = await stock.ConvertAsync(order.Id.Value, ct)
                    ? Result.Success
                    : await stock.ReacquireAsSoldAsync(order.Id.Value, ct);

                if (portions.IsFailure)
                {
                    LogPortionsGone(logger, order.Id.Value, @event.PaymentId);
                    order.FlagUnusablePayment(@event.PaymentId, @event.Amount, "Paid after the hold ran out and the portions were sold to others.");
                    return await SaveAsync(ct);
                }

                order.MarkPaid(@event.PaymentId, @event.MpesaReceipt, @event.Late, time.GetUtcNow());
                paidNow = true;

                return await SaveAsync(ct);
            },
            cancellationToken);

        HandlerRetryException.ThrowIfFailed(result);

        if (paidNow)
        {
            await notifier.OrderPaidAsync(order, CancellationToken.None);
            await realtime.OrderChangedAsync(order, CancellationToken.None);
        }
    }

    private async Task<Result<bool>> SaveAsync(CancellationToken cancellationToken)
    {
        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        return saved.IsFailure ? saved.Error! : Result<bool>.Success(true);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Kraft order {OrderId} was paid (payment {PaymentId}) after its portions were sold: flagged for refund.")]
    private static partial void LogPortionsGone(ILogger logger, Guid orderId, Guid paymentId);
}

/// <summary>
///     <c>PaymentFailed</c> for a Kraft order: the order keeps its hold so the customer can try again until the hold
///     runs out.
/// </summary>
public sealed class KraftOrderPaymentFailedHandler(
    IKraftOrderRepository orders,
    ICommerceUnitOfWork unitOfWork)
    : IIntegrationEventHandler<PaymentFailed>
{
    public const string HandlerName = "commerce.order-payment-failed";

    public async Task HandleAsync(PaymentFailed @event, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@event);

        if (@event.Purpose != PaymentPurpose.KraftOrder ||
            await unitOfWork.AlreadyProcessedAsync(@event.Id, HandlerName, cancellationToken))
        {
            return;
        }

        var order = await orders.FindAsync(new KraftOrderId(@event.ReferenceId), cancellationToken);

        unitOfWork.MarkProcessed(@event.Id, HandlerName);
        order?.PaymentFailed(@event.PaymentId, @event.Reason.ToString());

        HandlerRetryException.ThrowIfFailed(await unitOfWork.SaveChangesAsync(cancellationToken));
    }
}
