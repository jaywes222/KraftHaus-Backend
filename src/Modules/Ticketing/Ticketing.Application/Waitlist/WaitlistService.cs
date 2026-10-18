using Common.Application.EventCatalog;
using Common.Domain.ResultMonad;
using Microsoft.Extensions.Options;
using Ticketing.Application.Abstractions;
using Ticketing.Domain.Common;
using Ticketing.Domain.Waitlist;

namespace Ticketing.Application.Waitlist;

/// <summary>UF-001: a visitor joins an event's waitlist from the public site (anonymous, tenant from the host).</summary>
public sealed class WaitlistService(
    IEventCatalog events,
    IWaitlistRepository waitlist,
    ITicketingUnitOfWork unitOfWork,
    IWaitlistConfirmationSender confirmation,
    IOptions<WaitlistOptions> options,
    TimeProvider time)
{
    /// <summary>
    ///     Order of checks: the event must exist and be public (404), its waitlist open (409 <c>WaitlistClosed</c>),
    ///     the form valid (400 with every field problem), and the phone new for this event (409
    ///     <c>WaitlistAlreadyJoined</c>, no second SMS). The confirmation SMS is attempted after the entry is saved.
    /// </summary>
    public async Task<Result<WaitlistJoinedView>> JoinAsync(
        JoinWaitlistRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var @event = string.IsNullOrWhiteSpace(request.EventSlug)
            ? null
            : await events.FindBySlugAsync(request.EventSlug.Trim(), cancellationToken);

        if (@event is null || @event.Status == EventLifecycle.Draft)
        {
            return TicketingErrors.EventNotFound;
        }

        var now = time.GetUtcNow();

        if (!WaitlistPolicy.IsOpen(@event, now))
        {
            return TicketingErrors.WaitlistClosed;
        }

        var joined = WaitlistEntry.Join(
            new WaitlistEntryId(Guid.CreateVersion7(now)),
            @event.EventId,
            new WaitlistApplicant(
                request.Name,
                request.Phone,
                request.Age,
                request.Gender.ToDomain(),
                request.Location,
                request.Email,
                request.PrivacyAccepted,
                request.PrivacyPolicyVersion,
                request.MarketingConsent),
            options.Value.MinimumAge,
            now);

        if (joined.IsFailure)
        {
            return joined.Error!;
        }

        var entry = joined.Value!;

        if (await waitlist.ExistsAsync(@event.EventId, entry.Phone, cancellationToken))
        {
            return TicketingErrors.WaitlistAlreadyJoined;
        }

        waitlist.Add(entry);

        var saved = await unitOfWork.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return saved.Error!;
        }

        // Not tied to the request: once the entry is saved, a disconnecting client must not cancel the SMS.
        var smsSent = await confirmation.SendAsync(entry, @event.Title, CancellationToken.None);

        if (smsSent)
        {
            entry.MarkConfirmationSent(time.GetUtcNow());
        }
        else
        {
            // Left for the resend job.
            entry.RecordConfirmationFailed();
        }

        // A failure here only leaves the bookkeeping behind: the visitor is on the list either way.
        _ = await unitOfWork.SaveChangesAsync(CancellationToken.None);

        return new WaitlistJoinedView(entry.Id.Value, smsSent);
    }
}
