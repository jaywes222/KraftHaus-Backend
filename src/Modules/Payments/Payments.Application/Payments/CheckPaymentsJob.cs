using Common.Application.Messaging;
using Microsoft.Extensions.Options;
using Payments.Application.Abstractions;
using Payments.Domain.Payments;

namespace Payments.Application.Payments;

/// <summary>
///     Safety net for missed callbacks (Spec 08 §9.4, PalPluss polling guidance): checks payments still waiting, within
///     the PalPluss rate-limit budget, and stops waiting after <c>PendingTimeoutMinutes</c>.
/// </summary>
public sealed class CheckPaymentsJob(
    IPaymentRepository payments,
    PaymentStatusSync sync,
    IOptions<PaymentsOptions> options,
    TimeProvider time)
    : ITenantJob
{
    public const string Name = "payments.check-pending";

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = time.GetUtcNow();

        var due = await payments.ListDueForCheckAsync(
            createdBefore: now.AddSeconds(-settings.FirstCheckAfterSeconds),
            checkedBefore: now.AddSeconds(-settings.CheckIntervalSeconds),
            expiredAfter: now.AddMinutes(-settings.LateSuccessWindowMinutes),
            max: settings.MaxChecksPerRun,
            cancellationToken);

        foreach (var payment in due)
        {
            var reachable = await sync.SyncAsync(payment, cancellationToken);

            var waitingTooLong = payment.Status is PaymentStatus.Initiating or PaymentStatus.Pending &&
                                 payment.CreatedAt <= now.AddMinutes(-settings.PendingTimeoutMinutes);

            if (reachable && waitingTooLong)
            {
                await sync.ExpireAsync(payment, cancellationToken);
            }
        }
    }
}
