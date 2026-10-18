using Common.Application.Ticketing;
using Ticketing.Application.Abstractions;

namespace Ticketing.Application.Tickets;

/// <summary><see cref="ITicketHolders" /> for other modules.</summary>
public sealed class TicketHolders(ITicketRepository tickets) : ITicketHolders
{
    public Task<bool> HoldsTicketAsync(string phone, Guid eventId, CancellationToken cancellationToken) =>
        tickets.HoldsUsableTicketAsync(phone, eventId, cancellationToken);

    public Task<bool> HoldsTicketForDateAsync(string phone, Guid eventDateId, CancellationToken cancellationToken) =>
        tickets.HoldsUsableTicketForDateAsync(phone, eventDateId, cancellationToken);
}
