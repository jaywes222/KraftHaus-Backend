using Common.Application.EventCatalog;
using Common.Application.Messaging;
using Microsoft.Extensions.Options;
using Ticketing.Application.Abstractions;
using Ticketing.Application.Checkout;
using Ticketing.Application.Tickets;
using Ticketing.Domain.Orders;

namespace Ticketing.Application.Orders;

/// <summary>
///     Spec 08 §8.4 / §9.4 (hourly): reminds Lipa Mdogo Mdogo customers before the balance is due, and cancels orders
///     still unpaid <c>OverdueGraceHours</c> after it: tickets are voided and the seats go back on sale.
/// </summary>
public sealed class BalanceRemindersJob(
    IOrderRepository orders,
    ITicketRepository tickets,
    ICapacityLedger capacity,
    IEventCatalog events,
    ITicketingUnitOfWork unitOfWork,
    ITicketNotifier notifier,
    IOptions<LipaMdogoMdogoOptions> options,
    TimeProvider time)
    : ITenantJob
{
    public const string Name = "ticketing.lipa-mdogo-mdogo";

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = time.GetUtcNow();
        var horizon = settings.ReminderDaysBeforeDue.DefaultIfEmpty(0).Max();
        var titles = new Dictionary<Guid, string>();

        foreach (var order in await orders.ListBalanceDueAsync(now.AddDays(horizon), settings.BatchSize, cancellationToken))
        {
            var due = order.BalanceDueAt!.Value;
            var title = await TitleAsync(order.EventId, titles, cancellationToken);

            if (now > due.AddHours(settings.OverdueGraceHours))
            {
                await CancelAsync(order, title, cancellationToken);
                continue;
            }

            // One reminder per slot reached so far (e.g. 3 days and 1 day before), never repeated.
            var reached = settings.ReminderDaysBeforeDue.Count(days => now >= due.AddDays(-days));
            if (order.BalanceRemindersSent < reached)
            {
                for (var i = order.BalanceRemindersSent; i < reached; i++)
                {
                    order.RecordBalanceReminder();
                }

                await unitOfWork.SaveChangesAsync(cancellationToken);
                await notifier.BalanceReminderAsync(order, title, CancellationToken.None);
            }
        }
    }

    private async Task CancelAsync(Order order, string title, CancellationToken cancellationToken)
    {
        var cancelled = await unitOfWork.InTransactionAsync<bool>(
            async ct =>
            {
                if (!order.CancelForUnpaidBalance(time.GetUtcNow()))
                {
                    return false;
                }

                foreach (var ticket in await tickets.ListForOrderAsync(order.Id, ct))
                {
                    ticket.Void("Lipa Mdogo Mdogo balance not paid");
                }

                await capacity.ReturnSoldAsync(order.Id, ct);

                var saved = await unitOfWork.SaveChangesAsync(ct);
                if (saved.IsFailure)
                {
                    return saved.Error!;
                }

                return true;
            },
            cancellationToken);

        if (cancelled is { IsFailure: false, Value: true })
        {
            await notifier.OrderCancelledForBalanceAsync(order, title, CancellationToken.None);
        }
    }

    private async Task<string> TitleAsync(Guid eventId, Dictionary<Guid, string> cache, CancellationToken cancellationToken)
    {
        if (!cache.TryGetValue(eventId, out var title))
        {
            title = (await events.GetSaleSnapshotAsync(eventId, cancellationToken))?.Title ?? "your event";
            cache[eventId] = title;
        }

        return title;
    }
}
