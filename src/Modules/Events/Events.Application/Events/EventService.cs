using Common.Domain.Currency;
using Common.Domain.ResultMonad;
using Events.Application.Abstractions;
using Events.Domain.Common;
using Events.Domain.Model;
using Events.Domain.Venues;

namespace Events.Application.Events;

/// <summary>
///     Admin use cases for events (UF-016): create and edit, venue, entrances, dates, tiers, sale phases, group
///     offers, policies and the lifecycle. Every write loads the aggregate, applies one domain method, saves (the
///     audit rows are written in the same transaction) and returns the updated <see cref="EventView" />.
/// </summary>
public sealed class EventService(
    IEventRepository events,
    IVenueRepository venues,
    IEventsUnitOfWork unitOfWork,
    TimeProvider time)
{
    // ---------------------------------------------------------------------------------------------------------
    // Reads
    // ---------------------------------------------------------------------------------------------------------

    public async Task<Result<IReadOnlyList<EventSummaryView>>> ListAsync(
        EventStatus? status,
        CancellationToken cancellationToken)
    {
        var list = await events.ListForReadAsync(status, cancellationToken);

        return Result<IReadOnlyList<EventSummaryView>>.Success([.. list.Select(EventSummaryView.From)]);
    }

    public async Task<Result<EventView>> GetAsync(EventId id, CancellationToken cancellationToken)
    {
        var @event = await events.FindForReadAsync(id, cancellationToken);

        return @event is null
            ? EventsErrors.NotFound("Event", id.Value)
            : EventView.From(@event, time.GetUtcNow());
    }

    // ---------------------------------------------------------------------------------------------------------
    // Details, policies, venue
    // ---------------------------------------------------------------------------------------------------------

    public async Task<Result<EventView>> CreateAsync(EventDetailsInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var created = Event.Create(
            new EventId(NewGuid()),
            input.Slug,
            input.Title,
            input.Summary,
            input.Description,
            input.PosterUrl);

        if (created.IsFailure)
        {
            return created.Error!;
        }

        var @event = created.Value!;

        if (await events.SlugExistsAsync(@event.Slug, except: null, cancellationToken))
        {
            return SlugTaken();
        }

        events.Add(@event);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return EventView.From(@event, time.GetUtcNow());
    }

    public async Task<Result<EventView>> UpdateDetailsAsync(
        EventId id,
        EventDetailsInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var slug = Event.ValidateSlug(input.Slug);

        if (!slug.IsFailure && await events.SlugExistsAsync(slug.Value!, id, cancellationToken))
        {
            return SlugTaken();
        }

        return await ChangeAsync(
            id,
            (@event, _) => @event.UpdateDetails(
                input.Slug,
                input.Title,
                input.Summary,
                input.Description,
                input.PosterUrl),
            cancellationToken);
    }

    public Task<Result<EventView>> UpdatePoliciesAsync(
        EventId id,
        PoliciesInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        return ChangeAsync(
            id,
            (@event, _) =>
            {
                var policies = EventPolicies.Create(
                    input.RefundsEnabled,
                    input.TransfersEnabled,
                    input.MaxTransfersPerTicket,
                    input.LipaMdogoMdogoEnabled,
                    input.HoldMinutes,
                    input.MaxTicketsPerOrder);

                return policies.IsFailure ? policies.Error! : @event.UpdatePolicies(policies.Value!);
            },
            cancellationToken);
    }

    /// <summary>Copies a library venue (and its entrances) onto the event.</summary>
    public async Task<Result<EventView>> UseVenueAsync(
        EventId id,
        UseVenueInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var venue = await venues.FindForReadAsync(new VenueId(input.VenueId), cancellationToken);
        if (venue is null)
        {
            return EventsErrors.NotFound("Venue", input.VenueId);
        }

        return await ChangeAsync(
            id,
            (@event, _) => @event.UseVenue(venue, () => new EventEntranceId(NewGuid())),
            cancellationToken);
    }

    public Task<Result<EventView>> UseInlineVenueAsync(
        EventId id,
        InlineVenueInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        return ChangeAsync(
            id,
            (@event, _) => WithVenueDetails(input, @event.UseInlineVenue),
            cancellationToken);
    }

    /// <summary>Edits the event's own venue copy; the library venue is never changed.</summary>
    public Task<Result<EventView>> UpdateVenueAsync(
        EventId id,
        InlineVenueInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        return ChangeAsync(
            id,
            (@event, _) => WithVenueDetails(input, @event.UpdateVenueDetails),
            cancellationToken);
    }

    public Task<Result<CreatedItemView>> AddEntranceAsync(
        EventId id,
        EntranceInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var entranceId = new EventEntranceId(NewGuid());

        return AddAsync(
            id,
            entranceId.Value,
            (@event, _) => @event.AddEntrance(entranceId, input.Name, input.Description),
            cancellationToken);
    }

    public Task<Result<EventView>> UpdateEntranceAsync(
        EventId id,
        EventEntranceId entranceId,
        EntranceInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        return ChangeAsync(
            id,
            (@event, _) => @event.UpdateEntrance(entranceId, input.Name, input.Description),
            cancellationToken);
    }

    public Task<Result<EventView>> RemoveEntranceAsync(
        EventId id,
        EventEntranceId entranceId,
        CancellationToken cancellationToken) =>
        ChangeAsync(id, (@event, _) => @event.RemoveEntrance(entranceId), cancellationToken);

    // ---------------------------------------------------------------------------------------------------------
    // Dates
    // ---------------------------------------------------------------------------------------------------------

    public Task<Result<CreatedItemView>> AddDateAsync(EventId id, DateInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var dateId = new EventDateId(NewGuid());

        return AddAsync(
            id,
            dateId.Value,
            (@event, now) => @event.AddDate(dateId, ToSpec(input), now),
            cancellationToken);
    }

    public Task<Result<EventView>> UpdateDateAsync(
        EventId id,
        EventDateId dateId,
        DateInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        return ChangeAsync(id, (@event, now) => @event.UpdateDate(dateId, ToSpec(input), now), cancellationToken);
    }

    public Task<Result<EventView>> RemoveDateAsync(EventId id, EventDateId dateId, CancellationToken cancellationToken) =>
        ChangeAsync(id, (@event, _) => @event.RemoveDate(dateId), cancellationToken);

    public Task<Result<EventView>> CancelDateAsync(EventId id, EventDateId dateId, CancellationToken cancellationToken) =>
        ChangeAsync(id, (@event, now) => @event.CancelDate(dateId, now), cancellationToken);

    // ---------------------------------------------------------------------------------------------------------
    // Tiers
    // ---------------------------------------------------------------------------------------------------------

    public Task<Result<CreatedItemView>> AddTierAsync(
        EventId id,
        EventDateId dateId,
        TierInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var tierId = new TicketTierId(NewGuid());

        return AddAsync(
            id,
            tierId.Value,
            (@event, _) => @event.AddTier(dateId, tierId, ToSpec(input)),
            cancellationToken);
    }

    public Task<Result<EventView>> UpdateTierAsync(
        EventId id,
        EventDateId dateId,
        TicketTierId tierId,
        TierInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        return ChangeAsync(id, (@event, _) => @event.UpdateTier(dateId, tierId, ToSpec(input)), cancellationToken);
    }

    public Task<Result<EventView>> RemoveTierAsync(
        EventId id,
        EventDateId dateId,
        TicketTierId tierId,
        CancellationToken cancellationToken) =>
        ChangeAsync(id, (@event, _) => @event.RemoveTier(dateId, tierId), cancellationToken);

    public Task<Result<EventView>> WithdrawTierAsync(
        EventId id,
        EventDateId dateId,
        TicketTierId tierId,
        CancellationToken cancellationToken) =>
        ChangeAsync(id, (@event, _) => @event.WithdrawTier(dateId, tierId), cancellationToken);

    // ---------------------------------------------------------------------------------------------------------
    // Sale phases
    // ---------------------------------------------------------------------------------------------------------

    public Task<Result<CreatedItemView>> AddPhaseAsync(
        EventId id,
        EventDateId dateId,
        TicketTierId tierId,
        PhaseInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var phaseId = new SalePhaseId(NewGuid());

        return AddAsync(
            id,
            phaseId.Value,
            (@event, now) => WithPrice(
                input.Price,
                price => @event.AddPhase(dateId, tierId, phaseId, ToSpec(input, price), now)),
            cancellationToken);
    }

    public Task<Result<EventView>> UpdatePhaseAsync(
        EventId id,
        EventDateId dateId,
        TicketTierId tierId,
        SalePhaseId phaseId,
        PhaseInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        return ChangeAsync(
            id,
            (@event, now) => WithPrice(
                input.Price,
                price => @event.UpdatePhase(dateId, tierId, phaseId, ToSpec(input, price), now)),
            cancellationToken);
    }

    public Task<Result<EventView>> RemovePhaseAsync(
        EventId id,
        EventDateId dateId,
        TicketTierId tierId,
        SalePhaseId phaseId,
        CancellationToken cancellationToken) =>
        ChangeAsync(id, (@event, now) => @event.RemovePhase(dateId, tierId, phaseId, now), cancellationToken);

    // ---------------------------------------------------------------------------------------------------------
    // Group offers
    // ---------------------------------------------------------------------------------------------------------

    public Task<Result<CreatedItemView>> AddGroupOfferAsync(
        EventId id,
        EventDateId dateId,
        TicketTierId tierId,
        GroupOfferInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        var offerId = new GroupOfferId(NewGuid());

        return AddAsync(
            id,
            offerId.Value,
            (@event, now) => WithPrice(
                input.Price,
                price => @event.AddGroupOffer(dateId, tierId, offerId, ToSpec(input, price), now)),
            cancellationToken);
    }

    public Task<Result<EventView>> UpdateGroupOfferAsync(
        EventId id,
        EventDateId dateId,
        TicketTierId tierId,
        GroupOfferId offerId,
        GroupOfferInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        return ChangeAsync(
            id,
            (@event, now) => WithPrice(
                input.Price,
                price => @event.UpdateGroupOffer(dateId, tierId, offerId, ToSpec(input, price), now)),
            cancellationToken);
    }

    public Task<Result<EventView>> RemoveGroupOfferAsync(
        EventId id,
        EventDateId dateId,
        TicketTierId tierId,
        GroupOfferId offerId,
        CancellationToken cancellationToken) =>
        ChangeAsync(id, (@event, now) => @event.RemoveGroupOffer(dateId, tierId, offerId, now), cancellationToken);

    public Task<Result<EventView>> WithdrawGroupOfferAsync(
        EventId id,
        EventDateId dateId,
        TicketTierId tierId,
        GroupOfferId offerId,
        CancellationToken cancellationToken) =>
        ChangeAsync(id, (@event, _) => @event.WithdrawGroupOffer(dateId, tierId, offerId), cancellationToken);

    // ---------------------------------------------------------------------------------------------------------
    // Lifecycle
    // ---------------------------------------------------------------------------------------------------------

    public Task<Result<EventView>> PublishAsync(EventId id, CancellationToken cancellationToken) =>
        ChangeAsync(id, (@event, now) => @event.Publish(now), cancellationToken);

    public Task<Result<EventView>> CancelAsync(EventId id, CancelEventInput input, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        return ChangeAsync(id, (@event, now) => @event.Cancel(input.Reason, now), cancellationToken);
    }

    public Task<Result<EventView>> CompleteAsync(EventId id, CancellationToken cancellationToken) =>
        ChangeAsync(id, (@event, now) => @event.Complete(now), cancellationToken);

    // ---------------------------------------------------------------------------------------------------------
    // Plumbing
    // ---------------------------------------------------------------------------------------------------------

    private async Task<Result<EventView>> ChangeAsync(
        EventId id,
        Func<Event, DateTimeOffset, Result> change,
        CancellationToken cancellationToken)
    {
        var @event = await events.FindAsync(id, cancellationToken);
        if (@event is null)
        {
            return EventsErrors.NotFound("Event", id.Value);
        }

        var now = time.GetUtcNow();

        var result = change(@event, now);
        if (result.IsFailure)
        {
            return result.Error!;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return EventView.From(@event, now);
    }

    private async Task<Result<CreatedItemView>> AddAsync(
        EventId id,
        Guid itemId,
        Func<Event, DateTimeOffset, Result> add,
        CancellationToken cancellationToken)
    {
        var changed = await ChangeAsync(id, add, cancellationToken);

        return changed.IsFailure
            ? changed.Error!
            : new CreatedItemView(itemId, changed.Value!);
    }

    private static Result WithVenueDetails(InlineVenueInput input, Func<VenueDetails, Result> apply)
    {
        var details = VenueDetails.Create(input.Name, input.AddressLine, input.City, input.MapUrl, input.Directions);

        return details.IsFailure ? details.Error! : apply(details.Value!);
    }

    private static Result WithPrice(decimal amount, Func<Money, Result> apply)
    {
        var price = Money.Kes(amount);

        return price.IsFailure ? price.Error! : apply(price.Value);
    }

    private static DateSpec ToSpec(DateInput input) =>
        new(input.Label, input.StartsAt, input.EndsAt, input.DoorsOpenAt);

    private static TierSpec ToSpec(TierInput input) =>
        new(input.Name, input.Description, input.Capacity);

    private static PhaseSpec ToSpec(PhaseInput input, Money price) =>
        new(input.Name, price, input.StartsAt, input.EndsAt, input.Allocation);

    private static OfferSpec ToSpec(GroupOfferInput input, Money price) =>
        new(input.Kind, input.Name, input.Admits, price, input.StartsAt, input.EndsAt, input.Allocation);

    private static Error SlugTaken() =>
        EventsErrors.Conflict("SlugTaken", "Another event already uses this slug.");

    private Guid NewGuid() => Guid.CreateVersion7(time.GetUtcNow());
}
