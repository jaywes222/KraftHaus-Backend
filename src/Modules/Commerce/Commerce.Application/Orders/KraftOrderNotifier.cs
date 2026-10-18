using Commerce.Domain.Orders;
using Common.Application.Notifications;
using Microsoft.Extensions.Logging;

namespace Commerce.Application.Orders;

public interface IKraftOrderNotifier
{
    /// <summary>SMS once an order is paid (with its reference); never fails the caller.</summary>
    Task OrderPaidAsync(KraftOrder order, CancellationToken cancellationToken);

    /// <summary>SMS when the kitchen marks an order ready (PRODUCT.md: READY notification); never fails the caller.</summary>
    Task OrderReadyAsync(KraftOrder order, CancellationToken cancellationToken);
}

/// <summary>SMS straight through <see cref="ISmsGateway" /> until Communications owns it (Spec 08 §9.5).</summary>
public sealed partial class KraftOrderNotifier(ISmsGateway sms, ILogger<KraftOrderNotifier> logger) : IKraftOrderNotifier
{
    public Task OrderPaidAsync(KraftOrder order, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);

        var when = order.Kind == KraftOrderKind.Reservation
            ? "Pick it up at the stall on the day: show the QR in your orders."
            : "We'll text you when it's ready.";

        return SendAsync(order, $"KRAFTHAUS: {order.KraftName} got your order {order.Reference} (KES {order.Total:0}). {when}", cancellationToken);
    }

    public Task OrderReadyAsync(KraftOrder order, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);

        return SendAsync(order, $"KRAFTHAUS: Order {order.Reference} is ready at {order.KraftName}. Show the QR in your orders to collect.", cancellationToken);
    }

    private async Task SendAsync(KraftOrder order, string text, CancellationToken cancellationToken)
    {
        try
        {
            await sms.SendAsync(new SmsMessage(order.CustomerPhone, text), cancellationToken);
        }
#pragma warning disable CA1031 // An SMS failure must never undo a paid or ready order.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogSmsFailed(logger, order.Id.Value, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Order SMS for Kraft order {OrderId} failed.")]
    private static partial void LogSmsFailed(ILogger logger, Guid orderId, Exception exception);
}
