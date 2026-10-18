using Commerce.Application.Abstractions;
using Commerce.Application.Stock;
using Commerce.Domain.Common;
using Commerce.Domain.Orders;
using Common.Application.Messaging;
using Common.Application.Payments;
using Common.Domain.ResultMonad;
using Microsoft.Extensions.Options;

namespace Commerce.Application.Orders;

/// <summary>
///     Every minute per tenant: orders not paid before their hold ran out are expired and their portions go back on
///     sale. A prompt still on the phone gets <see cref="CommerceOptions.PendingPaymentGraceMinutes" /> more.
/// </summary>
public sealed class ReleaseExpiredKraftOrdersJob(
    IKraftOrderRepository orders,
    IStockLedger stock,
    ICommerceUnitOfWork unitOfWork,
    IMobilePayments payments,
    IOptions<CommerceOptions> options,
    TimeProvider time)
    : ITenantJob
{
    public const string Name = "commerce.release-expired-orders";

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = time.GetUtcNow();
        var expired = await orders.ListHoldExpiredAsync(now, settings.ReleaseBatchSize, cancellationToken);

        foreach (var order in expired)
        {
            if (order.CurrentPaymentId is { } paymentId &&
                now < order.HoldExpiresAt.AddMinutes(settings.PendingPaymentGraceMinutes) &&
                await payments.FindAsync(paymentId, cancellationToken) is { Status: MobilePaymentStatus.Pending })
            {
                continue;
            }

            var current = order;

            for (var attempt = 0; attempt < 3; attempt++)
            {
                var target = current;
                var released = await unitOfWork.InTransactionAsync<bool>(
                    async ct =>
                    {
                        await stock.ReleaseAsync(target.Id.Value, ct);
                        target.Expire(now);
                        var saved = await unitOfWork.SaveChangesAsync(ct);
                        return saved.IsFailure ? saved.Error! : Result<bool>.Success(true);
                    },
                    cancellationToken);

                if (released.Error?.Key != CommerceErrors.ConcurrentUpdateKey)
                {
                    break;
                }

                // Lost a race with the payment handler: re-read and decide again.
                var fresh = await orders.FindAsync(order.Id, cancellationToken);
                if (fresh is null || fresh.Status != KraftOrderStatus.Ordered)
                {
                    break;
                }

                current = fresh;
            }
        }
    }
}

/// <summary>Every minute per tenant: Ready orders nobody collected for too long are flagged on the kitchen board.</summary>
public sealed class FlagUncollectedKraftOrdersJob(
    IKraftOrderRepository orders,
    ICommerceUnitOfWork unitOfWork,
    IOptions<CommerceOptions> options,
    TimeProvider time)
    : ITenantJob
{
    public const string Name = "commerce.flag-uncollected-orders";

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = time.GetUtcNow();
        var stale = await orders.ListUncollectedAsync(now.AddMinutes(-settings.UncollectedAfterMinutes), settings.ReleaseBatchSize, cancellationToken);

        foreach (var order in stale)
        {
            order.FlagUncollected(now);
        }

        if (stale.Count > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
    }
}
