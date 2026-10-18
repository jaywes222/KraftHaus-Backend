using Common.Application.Notifications;
using System.Globalization;
using Microsoft.Extensions.Logging;
using Ticketing.Domain.Orders;

namespace Ticketing.Application.Tickets;

/// <summary>Transactional SMS about tickets. Interim direct send until Communications exists; never fails the caller.</summary>
public interface ITicketNotifier
{
    Task TicketsIssuedAsync(Order order, string eventTitle, int count, CancellationToken cancellationToken);

    Task BalancePaidAsync(Order order, string eventTitle, CancellationToken cancellationToken);

    Task BalanceReminderAsync(Order order, string eventTitle, CancellationToken cancellationToken);

    Task OrderCancelledForBalanceAsync(Order order, string eventTitle, CancellationToken cancellationToken);

    Task TicketReceivedAsync(string toPhone, string fromName, string eventTitle, CancellationToken cancellationToken);
}

public sealed partial class TicketNotifier(ISmsGateway sms, ILogger<TicketNotifier> logger) : ITicketNotifier
{
    public Task TicketsIssuedAsync(Order order, string eventTitle, int count, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);

        var noun = count == 1 ? "ticket" : "tickets";
        var text = order.PaymentPlan == PaymentPlan.LipaMdogoMdogo
            ? $"Deposit received: {count} {noun} for {eventTitle} reserved. Pay KES {order.BalanceDue:N0} by {Day(order.BalanceDueAt)} to get your QR codes. Order {order.Reference}."
            : $"Paid: {count} {noun} for {eventTitle}. Order {order.Reference}. Sign in to see your {noun} and QR codes.";

        return SendAsync(order.CustomerPhone, text, order.Id.Value, cancellationToken);
    }

    public Task BalancePaidAsync(Order order, string eventTitle, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);

        return SendAsync(
            order.CustomerPhone,
            $"Fully paid: your tickets for {eventTitle} are ready. Sign in to see the QR codes. Order {order.Reference}.",
            order.Id.Value,
            cancellationToken);
    }

    public Task BalanceReminderAsync(Order order, string eventTitle, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);

        return SendAsync(
            order.CustomerPhone,
            $"Reminder: KES {order.BalanceDue:N0} balance for {eventTitle} is due {Day(order.BalanceDueAt)}. Unpaid orders are cancelled. Order {order.Reference}.",
            order.Id.Value,
            cancellationToken);
    }

    public Task OrderCancelledForBalanceAsync(Order order, string eventTitle, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(order);

        return SendAsync(
            order.CustomerPhone,
            $"Order {order.Reference} for {eventTitle} was cancelled because the balance was not paid in time. Contact support about your deposit.",
            order.Id.Value,
            cancellationToken);
    }

    public Task TicketReceivedAsync(string toPhone, string fromName, string eventTitle, CancellationToken cancellationToken) =>
        SendAsync(
            toPhone,
            $"{fromName} sent you a ticket for {eventTitle}. Sign in with this number to see it.",
            Guid.Empty,
            cancellationToken);

    /// <summary>Due dates are shown in Nairobi time.</summary>
    private static string Day(DateTimeOffset? instant) =>
        instant is null
            ? "the due date"
            : instant.Value.ToOffset(TimeSpan.FromHours(3)).ToString("ddd d MMM", CultureInfo.InvariantCulture);

    private async Task SendAsync(string phone, string text, Guid orderId, CancellationToken cancellationToken)
    {
        try
        {
            await sms.SendAsync(new SmsMessage(phone, text), cancellationToken);
        }
#pragma warning disable CA1031 // The order and tickets exist whether or not the SMS goes out; they are visible in the app.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LogSmsFailed(logger, orderId, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ticket SMS for order {OrderId} failed.")]
    private static partial void LogSmsFailed(ILogger logger, Guid orderId, Exception exception);
}
