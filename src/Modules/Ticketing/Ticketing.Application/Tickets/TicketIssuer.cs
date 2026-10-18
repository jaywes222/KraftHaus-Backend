using Common.Application.EventCatalog;
using Ticketing.Domain.Common;
using Ticketing.Domain.Orders;
using Ticketing.Domain.Tickets;

namespace Ticketing.Application.Tickets;

/// <summary>One ticket per admission of a paid order, held by the buyer's phone; a bundle's tickets share a bundle id.</summary>
public static class TicketIssuer
{
    public static IReadOnlyList<Ticket> Issue(Order order, EventSaleSnapshot sale, TicketStatus status, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(sale);

        var issued = new List<Ticket>(order.Admissions);

        foreach (var line in order.Lines)
        {
            var date = sale.Dates.FirstOrDefault(x => x.DateId == line.EventDateId);
            var tier = date?.Tiers.FirstOrDefault(x => x.TierId == line.TierId);

            for (var unit = 0; unit < line.Units; unit++)
            {
                var bundleId = line.Kind == OrderItemKind.Bundle ? Guid.CreateVersion7(now) : (Guid?)null;

                for (var admission = 0; admission < line.AdmissionsPerUnit; admission++)
                {
                    issued.Add(Ticket.Issue(
                        new TicketId(Guid.CreateVersion7(now)),
                        new TicketIssue(
                            order.Id,
                            line.Id,
                            order.EventId,
                            line.EventDateId,
                            line.TierId,
                            bundleId,
                            order.CustomerPhone,
                            order.CustomerName,
                            sale.Title,
                            tier?.Name ?? line.Name,
                            date?.Label,
                            date?.StartsAt ?? now,
                            date?.DoorsOpenAt,
                            sale.VenueName),
                        status,
                        now));
                }
            }
        }

        return issued;
    }
}
