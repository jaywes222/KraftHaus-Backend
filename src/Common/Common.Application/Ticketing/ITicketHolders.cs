namespace Common.Application.Ticketing;

/// <summary>Who holds tickets (implemented by Ticketing). Commerce checks it before taking a food order (O-04).</summary>
public interface ITicketHolders
{
    /// <summary>
    ///     True when <paramref name="phone" /> (E.164) holds a usable ticket for the event: valid or already checked in.
    ///     Tickets still awaiting a Lipa Mdogo Mdogo balance and void tickets do not count.
    /// </summary>
    Task<bool> HoldsTicketAsync(string phone, Guid eventId, CancellationToken cancellationToken);

    /// <summary>Like <see cref="HoldsTicketAsync" />, for one event date: live orders need a ticket for the date that is on.</summary>
    Task<bool> HoldsTicketForDateAsync(string phone, Guid eventDateId, CancellationToken cancellationToken);
}
