using Common.Application.Customers;
using Common.Application.EventCatalog;
using Common.Domain.ResultMonad;
using Common.Domain.StronglyTypedIds;
using Ticketing.Application.Abstractions;
using Ticketing.Domain.Common;
using Ticketing.Domain.Tickets;
using Common.Domain.Phones;

namespace Ticketing.Application.Tickets;

/// <summary>Body of <c>POST /api/client/tickets/{id}/transfer</c>.</summary>
public sealed record TransferTicketRequest(string? ToPhone, string? ToName);

/// <summary>Body of <c>PUT /api/client/tickets/{id}/attendee</c>; an empty name clears it.</summary>
public sealed record AssignAttendeeRequest(string? Name);

/// <param name="ToPhoneLast3">Confirmation for the sender; the full number is theirs to know already.</param>
public sealed record TransferResultView(Guid TicketId, string ToPhoneLast3, int TransfersUsed);

/// <summary>
///     What a ticket holder can do with their ticket: transfer it (UF-006, P-06) or name the attendee. Transfers are
///     final: one transaction rebinds the ticket to the recipient's phone, the sender loses it at once, the code (and
///     so the QR) changes, and the allowance goes down (Spec 08 §8.5).
/// </summary>
public sealed class TicketHolderService(
    ICustomerDirectory customers,
    ITicketRepository tickets,
    IEventCatalog events,
    ITicketingUnitOfWork unitOfWork,
    ITicketNotifier notifier,
    TicketQrCodec qr,
    TimeProvider time)
{
    public const int NameMaxLength = 80;

    public async Task<Result<TransferResultView>> TransferAsync(
        ApplicationUserId userId,
        TicketId ticketId,
        TransferTicketRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var owned = await OwnedTicketAsync(userId, ticketId, cancellationToken);
        if (owned.IsFailure)
        {
            return owned.Error!;
        }

        var (ticket, holderName) = owned.Value;

        if (!KenyanMobile.TryCreate(request.ToPhone, out var to))
        {
            return TicketingErrors.Invalid("TransferInvalid", ["ToPhone: Enter the recipient's Kenyan mobile number."]);
        }

        var toName = request.ToName?.Trim();
        if (toName is { Length: > NameMaxLength } || toName?.Any(char.IsControl) == true)
        {
            return TicketingErrors.Invalid("TransferInvalid", [$"ToName: At most {NameMaxLength} characters."]);
        }

        var sale = await events.GetSaleSnapshotAsync(ticket.EventId, cancellationToken);
        if (sale is null || !sale.Policies.TransfersEnabled)
        {
            return TicketingErrors.Conflict("TransfersDisabled", "Tickets for this event cannot be transferred.");
        }

        // Transfers close when the ticket goes live (midnight on the event day): from then the QR is out.
        if (time.GetUtcNow() >= qr.TransfersCloseAt(ticket))
        {
            return TicketingErrors.Conflict("TransferClosed", "Transfers close at midnight on the event day, when your QR goes live.");
        }

        switch (ticket.Transfer(to!.Value, toName ?? "Guest", sale.Policies.MaxTransfersPerTicket))
        {
            case TransferOutcome.SameHolder:
                return TicketingErrors.Invalid("TransferInvalid", ["ToPhone: This ticket is already yours."]);
            case TransferOutcome.LimitReached:
                return TicketingErrors.Conflict("TransferLimitReached", "This ticket has already been transferred the maximum number of times.");
            case TransferOutcome.NotTransferable:
                return TicketingErrors.Conflict("TicketNotTransferable", "Only valid, unused, fully paid tickets can be transferred.");
        }

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error!;
        }

        await notifier.TicketReceivedAsync(to.Value, holderName, sale.Title, CancellationToken.None);

        return new TransferResultView(ticket.Id.Value, to.Value[^3..], ticket.TransferCount);
    }

    public async Task<Result<MyTicketView>> AssignAttendeeAsync(
        ApplicationUserId userId,
        TicketId ticketId,
        AssignAttendeeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var owned = await OwnedTicketAsync(userId, ticketId, cancellationToken);
        if (owned.IsFailure)
        {
            return owned.Error!;
        }

        var ticket = owned.Value.Ticket;
        var name = request.Name?.Trim();

        if (name is { Length: > NameMaxLength } || name?.Any(char.IsControl) == true)
        {
            return TicketingErrors.Invalid("AttendeeInvalid", [$"Name: At most {NameMaxLength} characters."]);
        }

        if (ticket.Status is TicketStatus.Void or TicketStatus.CheckedIn)
        {
            return TicketingErrors.Conflict("TicketNotEditable", "This ticket can no longer be changed.");
        }

        ticket.AssignAttendee(name);

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error!;
        }

        return MyTicketView.For(ticket, qr, time.GetUtcNow());
    }

    private async Task<Result<(Ticket Ticket, string HolderName)>> OwnedTicketAsync(
        ApplicationUserId userId,
        TicketId ticketId,
        CancellationToken cancellationToken)
    {
        var profile = await customers.FindAsync(userId, cancellationToken);
        var ticket = await tickets.FindAsync(ticketId, cancellationToken);

        if (profile?.Phone is null || ticket is null ||
            !KenyanMobile.TryCreate(profile.Phone, out var phone) ||
            !string.Equals(ticket.HolderPhone, phone!.Value, StringComparison.Ordinal))
        {
            return TicketingErrors.NotFound("TicketNotFound", "The ticket was not found.");
        }

        return (ticket, profile.DisplayName);
    }
}
