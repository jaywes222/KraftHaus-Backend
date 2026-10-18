using Ticketing.Domain.Tickets;

namespace Ticketing.Application.Tickets;

/// <summary>
///     A ticket as its holder sees it. <see cref="QrPayload" /> is the text to render as a QR code; it is <c>null</c>
///     before <see cref="LiveAt" /> (midnight in Nairobi on the event day), while a Lipa Mdogo Mdogo balance is unpaid
///     (BR-010) and for void tickets. <see cref="Code" /> is hidden until then too.
/// </summary>
public sealed record MyTicketView(
    Guid Id,
    string? Code,
    string Status,
    Guid EventId,
    string EventTitle,
    Guid EventDateId,
    string? DateLabel,
    DateTimeOffset StartsAt,
    DateTimeOffset? DoorsOpenAt,
    string? VenueName,
    string TierName,
    string HolderName,
    string? AttendeeName,
    Guid? BundleId,
    int TransfersUsed,
    DateTimeOffset IssuedAt,
    DateTimeOffset? CheckedInAt,
    string? QrPayload,
    DateTimeOffset LiveAt,
    bool CanTransfer)
{
    /// <summary>The holder's view at <paramref name="now" />: the QR and code only once the ticket is live.</summary>
    public static MyTicketView For(Ticket ticket, TicketQrCodec qr, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(qr);

        var liveAt = qr.LiveAt(ticket);
        var live = now >= liveAt;
        var scannable = live && ticket.Status is TicketStatus.Valid or TicketStatus.CheckedIn;
        var canTransfer = ticket.Status == TicketStatus.Valid && now < qr.TransfersCloseAt(ticket);

        return From(ticket, scannable ? qr.Issue(ticket) : null, live, liveAt, canTransfer);
    }

    private static MyTicketView From(Ticket ticket, string? qrPayload, bool live, DateTimeOffset liveAt, bool canTransfer)
    {
        return new MyTicketView(
            ticket.Id.Value,
            live ? ticket.Code : null,
            ticket.Status.ToString(),
            ticket.EventId,
            ticket.EventTitle,
            ticket.EventDateId,
            ticket.DateLabel,
            ticket.DateStartsAt,
            ticket.DoorsOpenAt,
            ticket.VenueName,
            ticket.TierName,
            ticket.HolderName,
            ticket.AttendeeName,
            ticket.BundleId,
            ticket.TransferCount,
            ticket.IssuedAt,
            ticket.CheckedInAt,
            qrPayload,
            liveAt,
            canTransfer);
    }
}
