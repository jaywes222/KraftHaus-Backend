using Common.Domain.ResultMonad;
using Common.Domain.StronglyTypedIds;
using Ticketing.Application.Abstractions;
using Ticketing.Application.Tickets;
using Ticketing.Domain.Common;
using Ticketing.Domain.Tickets;
using Common.Domain.Codes;

namespace Ticketing.Application.CheckIn;

/// <summary>
///     UF-012 / UF-013 at the door. Every answer is one of the closed set of outcomes (PRODUCT.md), returned as 200 so
///     the scanner shows the right screen. Two scanners on the same ticket cannot both admit it: the second save fails
///     its concurrency check and is answered from the database (Spec 08 §8.2). Offline check-in is out of scope: the
///     API fails clearly and staff retry.
/// </summary>
public sealed class CheckInService(
    ITicketRepository tickets,
    ITicketingUnitOfWork unitOfWork,
    TicketQrCodec qr,
    TimeProvider time)
{
    public const int MaxSearchResults = 20;

    public async Task<Result<CheckInResultView>> ScanAsync(
        ApplicationUserId staffId,
        CheckInScanRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Signature first: forgeries and old screenshots are rejected without touching the database.
        var content = qr.Read(request.Payload, time.GetUtcNow());
        if (content is null)
        {
            return CheckInResultView.Rejected(CheckInOutcome.Invalid);
        }

        var ticket = await tickets.FindAsync(new TicketId(content.TicketId), cancellationToken);
        if (ticket is null || !string.Equals(ticket.Code, content.Code, StringComparison.Ordinal))
        {
            // Unknown ticket, or a QR issued before a transfer (the code has changed since).
            return CheckInResultView.Rejected(CheckInOutcome.Invalid);
        }

        return await AdmitAsync(ticket, request.EventDateId, staffId, CheckInMethod.Qr, cancellationToken);
    }

    public async Task<Result<CheckInResultView>> ManualAsync(
        ApplicationUserId staffId,
        CheckInManualRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Ticket? ticket = null;

        if (request.TicketId is { } ticketId)
        {
            ticket = await tickets.FindAsync(new TicketId(ticketId), cancellationToken);
        }
        else
        {
            var code = ShortCode.Normalize(request.Code);
            if (code.Length == Ticket.CodeLength)
            {
                ticket = await tickets.FindByCodeAsync(code, cancellationToken);
            }
        }

        return ticket is null
            ? CheckInResultView.Rejected(CheckInOutcome.Invalid)
            : await AdmitAsync(ticket, request.EventDateId, staffId, CheckInMethod.Manual, cancellationToken);
    }

    public async Task<Result<IReadOnlyList<CheckInSearchHit>>> SearchAsync(
        Guid eventDateId,
        string? query,
        CancellationToken cancellationToken)
    {
        var trimmed = query?.Trim() ?? string.Empty;
        if (trimmed.Length < 3)
        {
            return TicketingErrors.Invalid("SearchTooShort", ["Q: Type at least 3 characters (code, last phone digits or name)."]);
        }

        var hits = await tickets.SearchAsync(eventDateId, trimmed.Length > 60 ? trimmed[..60] : trimmed, MaxSearchResults, cancellationToken);

        return Result<IReadOnlyList<CheckInSearchHit>>.Success([.. hits.Select(CheckInSearchHit.From)]);
    }

    public async Task<Result<CheckInStatsView>> StatsAsync(Guid eventDateId, CancellationToken cancellationToken)
    {
        var counts = await tickets.CountByStatusAsync(eventDateId, cancellationToken);

        int Count(TicketStatus status) => counts.TryGetValue(status, out var n) ? n : 0;

        return new CheckInStatsView(
            eventDateId,
            Count(TicketStatus.CheckedIn),
            Count(TicketStatus.Valid),
            Count(TicketStatus.AwaitingBalance));
    }

    private async Task<Result<CheckInResultView>> AdmitAsync(
        Ticket ticket,
        Guid eventDateId,
        ApplicationUserId staffId,
        CheckInMethod method,
        CancellationToken cancellationToken)
    {
        var outcome = ticket.CheckIn(eventDateId, staffId.Value, method, time.GetUtcNow());
        if (outcome != CheckInOutcome.Admitted)
        {
            return CheckInResultView.For(outcome, ticket);
        }

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (!saved.IsFailure)
        {
            return CheckInResultView.For(CheckInOutcome.Admitted, ticket);
        }

        if (saved.Error!.Key != TicketingErrors.ConcurrentUpdateKey)
        {
            return saved.Error;
        }

        // Another scanner got there first: answer from what is now in the database.
        var current = await tickets.FindForReadAsync(ticket.Id, cancellationToken);
        return current is null
            ? CheckInResultView.Rejected(CheckInOutcome.Invalid)
            : CheckInResultView.For(current.CheckInOutcomeFor(eventDateId), current);
    }
}

public sealed record CheckInScanRequest(Guid EventDateId, string? Payload);

/// <summary>Manual check-in by the code printed under the QR, or by the ticket id picked from a search.</summary>
public sealed record CheckInManualRequest(Guid EventDateId, string? Code, Guid? TicketId);

/// <param name="Outcome">Admitted, AlreadyCheckedIn, Invalid, WrongDate or NotFullyPaid.</param>
/// <param name="CheckedInAt">When it was admitted (for AlreadyCheckedIn: the first time).</param>
public sealed record CheckInResultView(
    string Outcome,
    Guid? TicketId,
    string? HolderName,
    string? AttendeeName,
    string? TierName,
    string? Code,
    Guid? EventDateId,
    string? DateLabel,
    DateTimeOffset? CheckedInAt)
{
    public static CheckInResultView Rejected(CheckInOutcome outcome) =>
        new(outcome.ToString(), null, null, null, null, null, null, null, null);

    public static CheckInResultView For(CheckInOutcome outcome, Ticket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        return outcome == CheckInOutcome.Invalid
            ? Rejected(outcome)
            : new CheckInResultView(
                outcome.ToString(),
                ticket.Id.Value,
                ticket.HolderName,
                ticket.AttendeeName,
                ticket.TierName,
                ticket.Code,
                ticket.EventDateId,
                ticket.DateLabel,
                ticket.CheckedInAt);
    }
}

/// <param name="HolderPhoneLast3">Enough to confirm with the guest; the full number is not shown at the door.</param>
public sealed record CheckInSearchHit(Guid TicketId, string Code, string HolderName, string HolderPhoneLast3, string TierName, string Status, DateTimeOffset? CheckedInAt)
{
    public static CheckInSearchHit From(Ticket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        return new CheckInSearchHit(ticket.Id.Value, ticket.Code, ticket.HolderName, ticket.HolderPhone[^3..], ticket.TierName, ticket.Status.ToString(), ticket.CheckedInAt);
    }
}

public sealed record CheckInStatsView(Guid EventDateId, int CheckedIn, int NotYetArrived, int AwaitingBalance);
