using Common.Application.Messaging;
using Common.Application.Payments;
using Microsoft.Extensions.Options;
using Ticketing.Application.Abstractions;
using Ticketing.Application.Checkout;
using Ticketing.Domain.Common;
using Ticketing.Domain.Orders;

namespace Ticketing.Application.Orders;

/// <summary>
///     Spec 08 §8.1 / §9.4: every minute, expire orders whose hold ran out and give their capacity back. An order whose
///     prompt is still pending on the phone gets <c>PendingPaymentGraceMinutes</c> more, so a customer typing their PIN
///     at the last second is not cut off; money that still arrives later is handled as a late payment.
/// </summary>
public sealed class ReleaseExpiredHoldsJob(
    IOrderRepository orders,
    ICapacityLedger capacity,
    ITicketingUnitOfWork unitOfWork,
    IMobilePayments payments,
    IOptions<OrderOptions> options,
    TimeProvider time)
    : ITenantJob
{
    public const string Name = "ticketing.release-expired-holds";

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = time.GetUtcNow();

        foreach (var order in await orders.ListHoldExpiredAsync(now, settings.ReleaseBatchSize, cancellationToken))
        {
            if (order.CurrentPaymentId is { } paymentId &&
                now < order.HoldExpiresAt.AddMinutes(settings.PendingPaymentGraceMinutes) &&
                await payments.FindAsync(paymentId, cancellationToken) is { Status: MobilePaymentStatus.Pending })
            {
                continue;
            }

            await ExpireAsync(order.Id, cancellationToken);
        }
    }

    private const int MaxAttempts = 3;

    /// <summary>
    ///     Expires one order and releases its holds. A payment outcome may update the same order at the same moment;
    ///     the losing save is retried on a fresh copy instead of waiting for the next run.
    /// </summary>
    private async Task ExpireAsync(OrderId orderId, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxAttempts; attempt++)
        {
            var order = await orders.FindAsync(orderId, cancellationToken);
            if (order is null)
            {
                return;
            }

            var result = await unitOfWork.InTransactionAsync<bool>(
                async ct =>
                {
                    if (!order.Expire(time.GetUtcNow()))
                    {
                        return false;
                    }

                    await capacity.ReleaseAsync(order.Id, ct);

                    var saved = await unitOfWork.SaveChangesAsync(ct);
                    if (saved.IsFailure)
                    {
                        return saved.Error!;
                    }

                    return true;
                },
                cancellationToken);

            if (result.IsFailure && result.Error!.Key == TicketingErrors.ConcurrentUpdateKey)
            {
                continue;
            }

            return;
        }
    }
}
