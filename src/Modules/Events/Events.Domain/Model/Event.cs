using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Common.Domain.Aggregates;
using Common.Domain.Currency;
using Common.Domain.ResultMonad;
using Events.Domain.Common;
using Events.Domain.DomainEvents;
using Events.Domain.Venues;

namespace Events.Domain.Model;

/// <summary>
///     An event a tenant sells tickets for (Spec 08 §4, UF-016): details, the venue copy and its entrances, one or
///     more dates, and per date the tiers with their sale phases and group offers, plus the event's policies.
///     <para>
///         Editing rules. While <see cref="EventStatus.Draft" /> anything can change. Once
///         <see cref="EventStatus.Published" />, things customers may already have bought are protected: nothing is
///         deleted (tiers and offers are withdrawn, dates cancelled), capacity only grows, and a phase or offer that
///         has started keeps its price and start. Cancelled and completed events are read-only.
///     </para>
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification = "\"Event\" is the domain term (Spec 08); the type is never consumed from Visual Basic.")]
public sealed partial class Event : AggregateRoot<EventId>
{
    public const int SlugMinLength = 3;
    public const int SlugMaxLength = 80;
    public const int TitleMaxLength = 150;
    public const int SummaryMaxLength = 300;
    public const int DescriptionMaxLength = 8000;
    public const int CancellationReasonMaxLength = 500;
    public const int MaxDates = 31;
    public const int MaxTiersPerDate = 20;
    public const int MaxPhasesPerTier = 10;
    public const int MaxOffersPerTier = 10;

    private readonly List<EventDate> _dates = [];
    private readonly List<EventEntrance> _entrances = [];

    private Event(EventId id)
        : base(id)
    {
    }

    /// <summary>URL-safe identifier, unique within the tenant (e.g. <c>krafthaus-001</c>).</summary>
    public string Slug { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    public string? Summary { get; private set; }

    public string? Description { get; private set; }

    public Uri? PosterUrl { get; private set; }

    public EventStatus Status { get; private set; }

    public EventVenue? Venue { get; private set; }

    public EventPolicies Policies { get; private set; } = EventPolicies.Default;

    public DateTimeOffset? PublishedAt { get; private set; }

    public DateTimeOffset? CancelledAt { get; private set; }

    public string? CancellationReason { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public IReadOnlyList<EventDate> Dates => _dates.AsReadOnly();

    public IReadOnlyList<EventEntrance> Entrances => _entrances.AsReadOnly();

    // ---------------------------------------------------------------------------------------------------------
    // Creation and details
    // ---------------------------------------------------------------------------------------------------------

    public static Result<Event> Create(
        EventId id,
        string? slug,
        string? title,
        string? summary,
        string? description,
        Uri? posterUrl)
    {
        var details = ValidateDetails(slug, title, summary, description, posterUrl);
        if (details.IsFailure)
        {
            return details.Error!;
        }

        var (validSlug, validTitle, validSummary, validDescription, validPoster) = details.Value;

        var @event = new Event(id)
        {
            Slug = validSlug,
            Title = validTitle,
            Summary = validSummary,
            Description = validDescription,
            PosterUrl = validPoster,
            Status = EventStatus.Draft,
            Policies = EventPolicies.Default
        };

        @event.RaiseEvent(new EventCreated(id, validSlug, validTitle));

        return @event;
    }

    /// <summary>Changes the public details. The slug is part of public links, so it is frozen once published.</summary>
    public Result UpdateDetails(
        string? slug,
        string? title,
        string? summary,
        string? description,
        Uri? posterUrl)
    {
        var editable = EnsureEditable();
        if (editable.IsFailure)
        {
            return editable;
        }

        var details = ValidateDetails(slug, title, summary, description, posterUrl);
        if (details.IsFailure)
        {
            return details.Error!;
        }

        var (validSlug, validTitle, validSummary, validDescription, validPoster) = details.Value;

        if (Status != EventStatus.Draft && !string.Equals(validSlug, Slug, StringComparison.Ordinal))
        {
            return EventsErrors.InvalidState("SlugFrozen", "The slug cannot change after the event is published.");
        }

        var change = new EventDetailsUpdated(Id, validSlug, validTitle);

        Slug = validSlug;
        Title = validTitle;
        Summary = validSummary;
        Description = validDescription;
        PosterUrl = validPoster;

        RaiseEvent(change);

        return Result.Success;
    }

    public Result UpdatePolicies(EventPolicies policies)
    {
        ArgumentNullException.ThrowIfNull(policies);

        var editable = EnsureEditable();
        if (editable.IsFailure)
        {
            return editable;
        }

        var change = new EventPoliciesUpdated(
            Id,
            policies.RefundsEnabled,
            policies.TransfersEnabled,
            policies.MaxTransfersPerTicket,
            policies.LipaMdogoMdogoEnabled,
            policies.HoldMinutes,
            policies.MaxTicketsPerOrder);

        Policies = policies;

        RaiseEvent(change);

        return Result.Success;
    }

    // ---------------------------------------------------------------------------------------------------------
    // Venue and entrances
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    ///     Copies a library venue (details and entrances) onto the event, replacing any previous venue copy. Only
    ///     while in draft: after publishing, edit the copy with <see cref="UpdateVenueDetails" />.
    /// </summary>
    public Result UseVenue(Venue venue, Func<EventEntranceId> newEntranceId)
    {
        ArgumentNullException.ThrowIfNull(venue);
        ArgumentNullException.ThrowIfNull(newEntranceId);

        var draft = EnsureDraft("VenueFrozen", "The venue can only be replaced while the event is a draft.");
        if (draft.IsFailure)
        {
            return draft;
        }

        if (venue.Status == VenueStatus.Archived)
        {
            return EventsErrors.InvalidState("VenueArchived", "An archived venue cannot be used for an event.");
        }

        var change = new EventVenueSet(Id, venue.Id, venue.Details.Name);

        Venue = EventVenue.From(venue.Id, venue.Details);

        _entrances.Clear();
        foreach (var entrance in venue.Entrances)
        {
            _entrances.Add(EventEntrance.Create(newEntranceId(), entrance.Name, entrance.Description));
        }

        RaiseEvent(change);

        return Result.Success;
    }

    /// <summary>Sets a venue typed directly on the event (not from the library). Only while in draft.</summary>
    public Result UseInlineVenue(VenueDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        var draft = EnsureDraft("VenueFrozen", "The venue can only be replaced while the event is a draft.");
        if (draft.IsFailure)
        {
            return draft;
        }

        var change = new EventVenueSet(Id, null, details.Name);

        Venue = EventVenue.From(null, details);
        _entrances.Clear();

        RaiseEvent(change);

        return Result.Success;
    }

    /// <summary>Edits the event's venue copy inline (e.g. fix directions). Never touches the library venue.</summary>
    public Result UpdateVenueDetails(VenueDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        var editable = EnsureEditable();
        if (editable.IsFailure)
        {
            return editable;
        }

        if (Venue is null)
        {
            return EventsErrors.InvalidState("VenueNotSet", "Set a venue before editing it.");
        }

        var change = new EventVenueUpdated(Id, details.Name);

        Venue = Venue.WithDetails(details);

        RaiseEvent(change);

        return Result.Success;
    }

    public Result<EventEntrance> AddEntrance(EventEntranceId entranceId, string? name, string? description)
    {
        var editable = EnsureEditable();
        if (editable.IsFailure)
        {
            return editable.Error!;
        }

        if (Venue is null)
        {
            return EventsErrors.InvalidState("VenueNotSet", "Set a venue before adding entrances.");
        }

        var valid = EntranceRules.Validate(name, description);
        if (valid.IsFailure)
        {
            return valid.Error!;
        }

        var (validName, validDescription) = valid.Value;

        if (_entrances.Count >= EntranceRules.MaxEntrances)
        {
            return EventsErrors.InvalidState(
                "TooManyEntrances",
                $"An event can have at most {EntranceRules.MaxEntrances} entrances.");
        }

        if (_entrances.Exists(x => string.Equals(x.Name, validName, StringComparison.OrdinalIgnoreCase)))
        {
            return EventsErrors.Conflict("EntranceNameTaken", "Another entrance already has this name.");
        }

        var change = new EventStructureChanged(Id, EventPart.Entrance, ChangeKind.Added, entranceId.Value);

        var entrance = EventEntrance.Create(entranceId, validName, validDescription);
        _entrances.Add(entrance);

        RaiseEvent(change);

        return entrance;
    }

    public Result UpdateEntrance(EventEntranceId entranceId, string? name, string? description)
    {
        var editable = EnsureEditable();
        if (editable.IsFailure)
        {
            return editable;
        }

        var entrance = _entrances.Find(x => x.Id == entranceId);
        if (entrance is null)
        {
            return EventsErrors.NotFound("Entrance", entranceId.Value);
        }

        var valid = EntranceRules.Validate(name, description);
        if (valid.IsFailure)
        {
            return valid.Error!;
        }

        var (validName, validDescription) = valid.Value;

        if (_entrances.Exists(x =>
                x.Id != entranceId && string.Equals(x.Name, validName, StringComparison.OrdinalIgnoreCase)))
        {
            return EventsErrors.Conflict("EntranceNameTaken", "Another entrance already has this name.");
        }

        var change = new EventStructureChanged(Id, EventPart.Entrance, ChangeKind.Updated, entranceId.Value);

        entrance.Update(validName, validDescription);

        RaiseEvent(change);

        return Result.Success;
    }

    /// <summary>Removes an entrance. Only while in draft: check-in may reference entrances once published.</summary>
    public Result RemoveEntrance(EventEntranceId entranceId)
    {
        var draft = EnsureDraft("EntranceFrozen", "Entrances can only be removed while the event is a draft.");
        if (draft.IsFailure)
        {
            return draft;
        }

        var entrance = _entrances.Find(x => x.Id == entranceId);
        if (entrance is null)
        {
            return EventsErrors.NotFound("Entrance", entranceId.Value);
        }

        var change = new EventStructureChanged(Id, EventPart.Entrance, ChangeKind.Removed, entranceId.Value);

        _entrances.Remove(entrance);

        RaiseEvent(change);

        return Result.Success;
    }

    // ---------------------------------------------------------------------------------------------------------
    // Dates
    // ---------------------------------------------------------------------------------------------------------

    public Result<EventDate> AddDate(EventDateId dateId, DateSpec spec, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var editable = EnsureEditable();
        if (editable.IsFailure)
        {
            return editable.Error!;
        }

        if (_dates.Count >= MaxDates)
        {
            return EventsErrors.InvalidState("TooManyDates", $"An event can have at most {MaxDates} dates.");
        }

        var valid = ValidateDate(spec, now, except: null);
        if (valid.IsFailure)
        {
            return valid.Error!;
        }

        var (label, startsAt, endsAt, doorsOpenAt) = valid.Value;

        var change = new EventStructureChanged(Id, EventPart.Date, ChangeKind.Added, dateId.Value);

        var date = EventDate.Create(dateId, label, startsAt, endsAt, doorsOpenAt);
        _dates.Add(date);

        RaiseEvent(change);

        return date;
    }

    /// <summary>
    ///     Reschedules or relabels a date. Once published, a date that has started cannot move, and sales windows
    ///     must still fit inside the new date.
    /// </summary>
    public Result UpdateDate(EventDateId dateId, DateSpec spec, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var found = FindEditableDate(dateId);
        if (found.IsFailure)
        {
            return found.Error!;
        }

        var date = found.Value!;

        if (Status == EventStatus.Published && now >= date.StartsAt)
        {
            return EventsErrors.InvalidState("DateStarted", "A date that has started cannot be changed.");
        }

        var valid = ValidateDate(spec, now, except: dateId);
        if (valid.IsFailure)
        {
            return valid.Error!;
        }

        var (label, startsAt, endsAt, doorsOpenAt) = valid.Value;

        var windowsOutside = date.Tiers.Any(tier =>
            tier.Phases.Any(x => x.EndsAt > endsAt) ||
            tier.Offers.Any(x => x.EndsAt > endsAt));

        if (windowsOutside)
        {
            return EventsErrors.InvalidState(
                "SalesWindowAfterDate",
                "Some sale phases or offers end after the new date ends. Shorten them first.");
        }

        var change = new EventStructureChanged(Id, EventPart.Date, ChangeKind.Updated, dateId.Value);

        date.Update(label, startsAt, endsAt, doorsOpenAt);

        RaiseEvent(change);

        return Result.Success;
    }

    /// <summary>Deletes a date with its tiers. Only while in draft; after publishing use <see cref="CancelDate" />.</summary>
    public Result RemoveDate(EventDateId dateId)
    {
        var draft = EnsureDraft("DateFrozen", "Dates can only be removed while the event is a draft. Cancel it instead.");
        if (draft.IsFailure)
        {
            return draft;
        }

        var date = _dates.Find(x => x.Id == dateId);
        if (date is null)
        {
            return EventsErrors.NotFound("Date", dateId.Value);
        }

        var change = new EventStructureChanged(Id, EventPart.Date, ChangeKind.Removed, dateId.Value);

        _dates.Remove(date);

        RaiseEvent(change);

        return Result.Success;
    }

    /// <summary>
    ///     Cancels one date of a published event (its tiers stop selling). The last scheduled date cannot be
    ///     cancelled this way: cancel the event instead.
    /// </summary>
    public Result CancelDate(EventDateId dateId, DateTimeOffset now)
    {
        if (Status != EventStatus.Published)
        {
            return EventsErrors.InvalidState(
                "EventNotPublished",
                "Only dates of a published event can be cancelled. Remove the date from a draft instead.");
        }

        var date = _dates.Find(x => x.Id == dateId);
        if (date is null)
        {
            return EventsErrors.NotFound("Date", dateId.Value);
        }

        if (date.Status == EventDateStatus.Cancelled)
        {
            return EventsErrors.InvalidState("DateCancelled", "The date is already cancelled.");
        }

        if (now >= date.EndsAt)
        {
            return EventsErrors.InvalidState("DateEnded", "A date that has ended cannot be cancelled.");
        }

        if (_dates.Count(x => x.Status == EventDateStatus.Scheduled) == 1)
        {
            return EventsErrors.InvalidState(
                "LastDate",
                "This is the event's only remaining date. Cancel the event instead.");
        }

        var change = new EventStructureChanged(Id, EventPart.Date, ChangeKind.Cancelled, dateId.Value);

        date.Cancel();

        RaiseEvent(change);

        return Result.Success;
    }

    // ---------------------------------------------------------------------------------------------------------
    // Tiers
    // ---------------------------------------------------------------------------------------------------------

    public Result<TicketTier> AddTier(EventDateId dateId, TicketTierId tierId, TierSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var found = FindEditableDate(dateId);
        if (found.IsFailure)
        {
            return found.Error!;
        }

        var date = found.Value!;

        if (date.Tiers.Count >= MaxTiersPerDate)
        {
            return EventsErrors.InvalidState(
                "TooManyTiers",
                $"A date can have at most {MaxTiersPerDate} tiers.");
        }

        var valid = ValidateTier(spec, date, except: null);
        if (valid.IsFailure)
        {
            return valid.Error!;
        }

        var (name, description, capacity) = valid.Value;

        var change = new EventStructureChanged(Id, EventPart.Tier, ChangeKind.Added, tierId.Value);

        var tier = TicketTier.Create(tierId, name, description, capacity, date.NextTierPosition);
        date.AddTier(tier);

        RaiseEvent(change);

        return tier;
    }

    /// <summary>Renames or resizes a tier. Once published, capacity can only grow (tickets may be sold).</summary>
    public Result UpdateTier(EventDateId dateId, TicketTierId tierId, TierSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var found = FindEditableTier(dateId, tierId);
        if (found.IsFailure)
        {
            return found.Error!;
        }

        var (date, tier) = found.Value;

        var valid = ValidateTier(spec, date, except: tierId);
        if (valid.IsFailure)
        {
            return valid.Error!;
        }

        var (name, description, capacity) = valid.Value;

        if (Status == EventStatus.Published && capacity < tier.Capacity)
        {
            return EventsErrors.InvalidState(
                "CapacityReduced",
                "Capacity cannot be reduced after the event is published.");
        }

        if (AllocationsExceed(tier, capacity))
        {
            return EventsErrors.InvalidState(
                "CapacityBelowAllocation",
                "Capacity is lower than the allocation of one of the tier's phases or offers.");
        }

        var change = new EventStructureChanged(Id, EventPart.Tier, ChangeKind.Updated, tierId.Value);

        tier.Update(name, description, capacity);

        RaiseEvent(change);

        return Result.Success;
    }

    /// <summary>Deletes a tier with its phases and offers. Only while in draft; after publishing withdraw it.</summary>
    public Result RemoveTier(EventDateId dateId, TicketTierId tierId)
    {
        var draft = EnsureDraft("TierFrozen", "Tiers can only be removed while the event is a draft. Withdraw it instead.");
        if (draft.IsFailure)
        {
            return draft;
        }

        var found = FindTier(dateId, tierId);
        if (found.IsFailure)
        {
            return found.Error!;
        }

        var (date, tier) = found.Value;

        var change = new EventStructureChanged(Id, EventPart.Tier, ChangeKind.Removed, tierId.Value);

        date.RemoveTier(tier);

        RaiseEvent(change);

        return Result.Success;
    }

    /// <summary>Stops a tier selling on a published event. Tickets already sold stay valid.</summary>
    public Result WithdrawTier(EventDateId dateId, TicketTierId tierId)
    {
        if (Status != EventStatus.Published)
        {
            return EventsErrors.InvalidState(
                "EventNotPublished",
                "Only tiers of a published event can be withdrawn. Remove the tier from a draft instead.");
        }

        var found = FindTier(dateId, tierId);
        if (found.IsFailure)
        {
            return found.Error!;
        }

        var (_, tier) = found.Value;

        if (tier.Status == SaleItemStatus.Withdrawn)
        {
            return EventsErrors.InvalidState("TierWithdrawn", "The tier is already withdrawn.");
        }

        var change = new EventStructureChanged(Id, EventPart.Tier, ChangeKind.Withdrawn, tierId.Value);

        tier.Withdraw();

        RaiseEvent(change);

        return Result.Success;
    }

    // ---------------------------------------------------------------------------------------------------------
    // Sale phases
    // ---------------------------------------------------------------------------------------------------------

    public Result<SalePhase> AddPhase(
        EventDateId dateId,
        TicketTierId tierId,
        SalePhaseId phaseId,
        PhaseSpec spec,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var found = FindEditableTier(dateId, tierId);
        if (found.IsFailure)
        {
            return found.Error!;
        }

        var (date, tier) = found.Value;

        if (tier.Status == SaleItemStatus.Withdrawn)
        {
            return EventsErrors.InvalidState("TierWithdrawn", "A withdrawn tier cannot get new phases.");
        }

        if (tier.Phases.Count >= MaxPhasesPerTier)
        {
            return EventsErrors.InvalidState(
                "TooManyPhases",
                $"A tier can have at most {MaxPhasesPerTier} sale phases.");
        }

        var valid = ValidatePhase(spec, date, tier, except: null, now);
        if (valid.IsFailure)
        {
            return valid.Error!;
        }

        var (name, window) = valid.Value;

        var change = new EventStructureChanged(Id, EventPart.SalePhase, ChangeKind.Added, phaseId.Value);

        var phase = SalePhase.Create(phaseId, name, spec.Price, window, spec.Allocation, spec.EarlyAccess);
        tier.AddPhase(phase);

        RaiseEvent(change);

        return phase;
    }

    /// <summary>
    ///     Changes a phase. Once published and started, its price and start are locked (customers have paid that
    ///     price); its end can still move, but not into the past.
    /// </summary>
    public Result UpdatePhase(
        EventDateId dateId,
        TicketTierId tierId,
        SalePhaseId phaseId,
        PhaseSpec spec,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var found = FindEditableTier(dateId, tierId);
        if (found.IsFailure)
        {
            return found.Error!;
        }

        var (date, tier) = found.Value;

        var phase = tier.FindPhase(phaseId);
        if (phase is null)
        {
            return EventsErrors.NotFound("SalePhase", phaseId.Value);
        }

        if (Status == EventStatus.Published && phase.Window.HasStarted(now) &&
            (spec.Price != phase.Price || spec.StartsAt.ToUniversalTime() != phase.StartsAt))
        {
            return EventsErrors.InvalidState(
                "PhaseStarted",
                "A sale phase that has started keeps its price and start time.");
        }

        var valid = ValidatePhase(spec, date, tier, except: phaseId, now);
        if (valid.IsFailure)
        {
            return valid.Error!;
        }

        var (name, window) = valid.Value;

        var change = new EventStructureChanged(Id, EventPart.SalePhase, ChangeKind.Updated, phaseId.Value);

        phase.Update(name, spec.Price, window, spec.Allocation, spec.EarlyAccess);

        RaiseEvent(change);

        return Result.Success;
    }

    /// <summary>Deletes a phase: in a draft, or on a published event before the phase starts.</summary>
    public Result RemovePhase(EventDateId dateId, TicketTierId tierId, SalePhaseId phaseId, DateTimeOffset now)
    {
        var found = FindEditableTier(dateId, tierId);
        if (found.IsFailure)
        {
            return found.Error!;
        }

        var (_, tier) = found.Value;

        var phase = tier.FindPhase(phaseId);
        if (phase is null)
        {
            return EventsErrors.NotFound("SalePhase", phaseId.Value);
        }

        if (Status == EventStatus.Published && phase.Window.HasStarted(now))
        {
            return EventsErrors.InvalidState(
                "PhaseStarted",
                "A sale phase that has started cannot be removed. End it early instead.");
        }

        var change = new EventStructureChanged(Id, EventPart.SalePhase, ChangeKind.Removed, phaseId.Value);

        tier.RemovePhase(phase);

        RaiseEvent(change);

        return Result.Success;
    }

    // ---------------------------------------------------------------------------------------------------------
    // Group offers (Couple / Gang)
    // ---------------------------------------------------------------------------------------------------------

    public Result<GroupOffer> AddGroupOffer(
        EventDateId dateId,
        TicketTierId tierId,
        GroupOfferId offerId,
        OfferSpec spec,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var found = FindEditableTier(dateId, tierId);
        if (found.IsFailure)
        {
            return found.Error!;
        }

        var (date, tier) = found.Value;

        if (tier.Status == SaleItemStatus.Withdrawn)
        {
            return EventsErrors.InvalidState("TierWithdrawn", "A withdrawn tier cannot get new offers.");
        }

        if (tier.Offers.Count >= MaxOffersPerTier)
        {
            return EventsErrors.InvalidState(
                "TooManyOffers",
                $"A tier can have at most {MaxOffersPerTier} group offers.");
        }

        var valid = ValidateOffer(spec, spec.Kind, date, tier, except: null, now);
        if (valid.IsFailure)
        {
            return valid.Error!;
        }

        var (name, window) = valid.Value;

        var change = new EventStructureChanged(Id, EventPart.GroupOffer, ChangeKind.Added, offerId.Value);

        var offer = GroupOffer.Create(offerId, spec.Kind, name, spec.Admits, spec.Price, window, spec.Allocation);
        tier.AddOffer(offer);

        RaiseEvent(change);

        return offer;
    }

    /// <summary>
    ///     Changes an offer. Its kind never changes. Once published and started, price, admissions and start are
    ///     locked; the end can still move, but not into the past.
    /// </summary>
    public Result UpdateGroupOffer(
        EventDateId dateId,
        TicketTierId tierId,
        GroupOfferId offerId,
        OfferSpec spec,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var found = FindEditableTier(dateId, tierId);
        if (found.IsFailure)
        {
            return found.Error!;
        }

        var (date, tier) = found.Value;

        var offer = tier.FindOffer(offerId);
        if (offer is null)
        {
            return EventsErrors.NotFound("GroupOffer", offerId.Value);
        }

        if (offer.Status == SaleItemStatus.Withdrawn)
        {
            return EventsErrors.InvalidState("OfferWithdrawn", "A withdrawn offer cannot be changed.");
        }

        if (spec.Kind != offer.Kind)
        {
            return EventsErrors.Invalid("OfferKind", "An offer's kind cannot change. Create a new offer instead.");
        }

        if (Status == EventStatus.Published && offer.Window.HasStarted(now) &&
            (spec.Price != offer.Price || spec.Admits != offer.Admits ||
             spec.StartsAt.ToUniversalTime() != offer.StartsAt))
        {
            return EventsErrors.InvalidState(
                "OfferStarted",
                "An offer that has started keeps its price, admissions and start time.");
        }

        var valid = ValidateOffer(spec, offer.Kind, date, tier, except: offerId, now);
        if (valid.IsFailure)
        {
            return valid.Error!;
        }

        var (name, window) = valid.Value;

        var change = new EventStructureChanged(Id, EventPart.GroupOffer, ChangeKind.Updated, offerId.Value);

        offer.Update(name, spec.Admits, spec.Price, window, spec.Allocation);

        RaiseEvent(change);

        return Result.Success;
    }

    /// <summary>Deletes an offer: in a draft, or on a published event before it starts.</summary>
    public Result RemoveGroupOffer(EventDateId dateId, TicketTierId tierId, GroupOfferId offerId, DateTimeOffset now)
    {
        var found = FindEditableTier(dateId, tierId);
        if (found.IsFailure)
        {
            return found.Error!;
        }

        var (_, tier) = found.Value;

        var offer = tier.FindOffer(offerId);
        if (offer is null)
        {
            return EventsErrors.NotFound("GroupOffer", offerId.Value);
        }

        if (Status == EventStatus.Published && offer.Window.HasStarted(now))
        {
            return EventsErrors.InvalidState(
                "OfferStarted",
                "An offer that has started cannot be removed. Withdraw it instead.");
        }

        var change = new EventStructureChanged(Id, EventPart.GroupOffer, ChangeKind.Removed, offerId.Value);

        tier.RemoveOffer(offer);

        RaiseEvent(change);

        return Result.Success;
    }

    /// <summary>Stops an offer selling on a published event. Bundles already sold stay valid.</summary>
    public Result WithdrawGroupOffer(EventDateId dateId, TicketTierId tierId, GroupOfferId offerId)
    {
        if (Status != EventStatus.Published)
        {
            return EventsErrors.InvalidState(
                "EventNotPublished",
                "Only offers of a published event can be withdrawn. Remove the offer from a draft instead.");
        }

        var found = FindTier(dateId, tierId);
        if (found.IsFailure)
        {
            return found.Error!;
        }

        var (_, tier) = found.Value;

        var offer = tier.FindOffer(offerId);
        if (offer is null)
        {
            return EventsErrors.NotFound("GroupOffer", offerId.Value);
        }

        if (offer.Status == SaleItemStatus.Withdrawn)
        {
            return EventsErrors.InvalidState("OfferWithdrawn", "The offer is already withdrawn.");
        }

        var change = new EventStructureChanged(Id, EventPart.GroupOffer, ChangeKind.Withdrawn, offerId.Value);

        offer.Withdraw();

        RaiseEvent(change);

        return Result.Success;
    }

    // ---------------------------------------------------------------------------------------------------------
    // Lifecycle
    // ---------------------------------------------------------------------------------------------------------

    /// <summary>
    ///     Makes the event visible to customers. Requires a venue and at least one future date, every scheduled date
    ///     must have an active tier, and every active tier must have a sale phase or an active group offer.
    /// </summary>
    public Result Publish(DateTimeOffset now)
    {
        if (Status != EventStatus.Draft)
        {
            return EventsErrors.InvalidState("NotDraft", "Only a draft event can be published.");
        }

        var problems = PublishProblems(now);
        if (problems.Count > 0)
        {
            return new Error
            {
                Key = "Events.NotReadyToPublish",
                Description = "The event is not ready to publish.",
                StatusCode = System.Net.HttpStatusCode.Conflict,
                SubErrors = [.. problems]
            };
        }

        var change = new EventPublished(Id, now);

        Status = EventStatus.Published;
        PublishedAt = now;

        RaiseEvent(change);

        return Result.Success;
    }

    public Result Cancel(string? reason, DateTimeOffset now)
    {
        if (Status is not (EventStatus.Draft or EventStatus.Published))
        {
            return EventsErrors.InvalidState("NotCancellable", "Only a draft or published event can be cancelled.");
        }

        var validReason = Text.Required(reason, "CancellationReason", CancellationReasonMaxLength);
        if (validReason.IsFailure)
        {
            return validReason.Error!;
        }

        var change = new EventCancelled(Id, validReason.Value!);

        Status = EventStatus.Cancelled;
        CancelledAt = now;
        CancellationReason = validReason.Value;

        RaiseEvent(change);

        return Result.Success;
    }

    /// <summary>Closes a published event after its last scheduled date has ended.</summary>
    public Result Complete(DateTimeOffset now)
    {
        if (Status != EventStatus.Published)
        {
            return EventsErrors.InvalidState("NotPublished", "Only a published event can be completed.");
        }

        var lastEnd = _dates
            .Where(x => x.Status == EventDateStatus.Scheduled)
            .Select(x => (DateTimeOffset?)x.EndsAt)
            .Max();

        if (lastEnd is { } end && now < end)
        {
            return EventsErrors.InvalidState("NotFinished", "The event cannot be completed before its last date ends.");
        }

        var change = new EventCompleted(Id);

        Status = EventStatus.Completed;
        CompletedAt = now;

        RaiseEvent(change);

        return Result.Success;
    }

    /// <summary>Everything that stops <see cref="Publish" />, as readable sentences (empty when ready).</summary>
    public IReadOnlyList<string> PublishProblems(DateTimeOffset now)
    {
        var problems = new List<string>();

        if (Venue is null)
        {
            problems.Add("Set a venue.");
        }

        var scheduled = _dates.Where(x => x.Status == EventDateStatus.Scheduled).ToList();

        if (scheduled.Count == 0)
        {
            problems.Add("Add at least one date.");
        }

        foreach (var date in scheduled.OrderBy(x => x.StartsAt))
        {
            var name = DescribeDate(date);

            if (date.StartsAt <= now)
            {
                problems.Add($"{name} has already started.");
            }

            var activeTiers = date.Tiers.Where(x => x.Status == SaleItemStatus.Active).ToList();

            if (activeTiers.Count == 0)
            {
                problems.Add($"{name} has no ticket tiers.");
            }

            problems.AddRange(
                activeTiers
                    .Where(tier => !tier.HasPricing)
                    .Select(tier => $"Tier '{tier.Name}' on {name} has no sale phase or group offer."));
        }

        return problems;
    }

    // ---------------------------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------------------------

    private Result EnsureEditable() =>
        Status is EventStatus.Cancelled or EventStatus.Completed
            ? EventsErrors.InvalidState("EventClosed", "A cancelled or completed event cannot be changed.")
            : Result.Success;

    private Result EnsureDraft(string key, string description)
    {
        var editable = EnsureEditable();
        if (editable.IsFailure)
        {
            return editable;
        }

        return Status == EventStatus.Draft
            ? Result.Success
            : EventsErrors.InvalidState(key, description);
    }

    private Result<EventDate> FindEditableDate(EventDateId dateId)
    {
        var editable = EnsureEditable();
        if (editable.IsFailure)
        {
            return editable.Error!;
        }

        var date = _dates.Find(x => x.Id == dateId);
        if (date is null)
        {
            return EventsErrors.NotFound("Date", dateId.Value);
        }

        return date.Status == EventDateStatus.Cancelled
            ? EventsErrors.InvalidState("DateCancelled", "A cancelled date cannot be changed.")
            : date;
    }

    private Result<(EventDate Date, TicketTier Tier)> FindTier(EventDateId dateId, TicketTierId tierId)
    {
        var date = _dates.Find(x => x.Id == dateId);
        if (date is null)
        {
            return EventsErrors.NotFound("Date", dateId.Value);
        }

        var tier = date.FindTier(tierId);

        return tier is null
            ? EventsErrors.NotFound("Tier", tierId.Value)
            : (date, tier);
    }

    private Result<(EventDate Date, TicketTier Tier)> FindEditableTier(EventDateId dateId, TicketTierId tierId)
    {
        var date = FindEditableDate(dateId);
        if (date.IsFailure)
        {
            return date.Error!;
        }

        var tier = date.Value!.FindTier(tierId);

        return tier is null
            ? EventsErrors.NotFound("Tier", tierId.Value)
            : (date.Value, tier);
    }

    private static Result<(string Slug, string Title, string? Summary, string? Description, Uri? Poster)>
        ValidateDetails(string? slug, string? title, string? summary, string? description, Uri? posterUrl)
    {
        var validSlug = ValidateSlug(slug);
        if (validSlug.IsFailure)
        {
            return validSlug.Error!;
        }

        var validTitle = Text.Required(title, "Title", TitleMaxLength);
        if (validTitle.IsFailure)
        {
            return validTitle.Error!;
        }

        var validSummary = Text.Optional(summary, "Summary", SummaryMaxLength);
        if (validSummary.IsFailure)
        {
            return validSummary.Error!;
        }

        var validDescription = Text.Optional(description, "Description", DescriptionMaxLength);
        if (validDescription.IsFailure)
        {
            return validDescription.Error!;
        }

        var validPoster = Text.OptionalAbsoluteUrl(posterUrl, "PosterUrl");
        if (validPoster.IsFailure)
        {
            return validPoster.Error!;
        }

        return (validSlug.Value!, validTitle.Value!, validSummary.Value, validDescription.Value, validPoster.Value);
    }

    /// <summary>Lower-case letters and digits separated by single hyphens, 3–80 characters.</summary>
    public static Result<string> ValidateSlug(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return EventsErrors.Required("Slug", "Slug is required.");
        }

        var trimmed = slug.Trim();

        return trimmed.Length is >= SlugMinLength and <= SlugMaxLength && SlugPattern().IsMatch(trimmed)
            ? trimmed
            : EventsErrors.Invalid(
                "Slug",
                $"Slug must be {SlugMinLength}-{SlugMaxLength} lower-case letters or digits separated by single hyphens.");
    }

    private Result<(string? Label, DateTimeOffset StartsAt, DateTimeOffset EndsAt, DateTimeOffset? DoorsOpenAt)>
        ValidateDate(DateSpec spec, DateTimeOffset now, EventDateId? except)
    {
        var label = Text.Optional(spec.Label, "DateLabel", EventDate.LabelMaxLength);
        if (label.IsFailure)
        {
            return label.Error!;
        }

        var startsAt = spec.StartsAt.ToUniversalTime();
        var endsAt = spec.EndsAt.ToUniversalTime();
        var doorsOpenAt = spec.DoorsOpenAt?.ToUniversalTime();

        if (endsAt <= startsAt)
        {
            return EventsErrors.Invalid("DateEnd", "A date must end after it starts.");
        }

        if (endsAt - startsAt > TimeSpan.FromHours(48))
        {
            return EventsErrors.Invalid("DateEnd", "A single date can last at most 48 hours. Add another date instead.");
        }

        if (startsAt <= now)
        {
            return EventsErrors.Invalid("DateStart", "A date must start in the future.");
        }

        if (doorsOpenAt is { } doors && (doors > startsAt || doors < startsAt.AddHours(-12)))
        {
            return EventsErrors.Invalid("DoorsOpen", "Doors must open within the 12 hours before the start.");
        }

        var overlaps = _dates.Exists(x =>
            x.Id != except &&
            x.Status == EventDateStatus.Scheduled &&
            x.StartsAt < endsAt &&
            startsAt < x.EndsAt);

        if (overlaps)
        {
            return EventsErrors.Conflict("DateOverlaps", "The date overlaps another date of this event.");
        }

        return (label.Value, startsAt, endsAt, doorsOpenAt);
    }

    private static Result<(string Name, string? Description, int Capacity)> ValidateTier(
        TierSpec spec,
        EventDate date,
        TicketTierId? except)
    {
        var name = Text.Required(spec.Name, "TierName", TicketTier.NameMaxLength);
        if (name.IsFailure)
        {
            return name.Error!;
        }

        var description = Text.Optional(spec.Description, "TierDescription", TicketTier.DescriptionMaxLength);
        if (description.IsFailure)
        {
            return description.Error!;
        }

        if (spec.Capacity is < 1 or > TicketTier.MaxCapacity)
        {
            return EventsErrors.Invalid("Capacity", $"Capacity must be between 1 and {TicketTier.MaxCapacity}.");
        }

        var duplicate = date.Tiers.Any(x =>
            x.Id != except && string.Equals(x.Name, name.Value, StringComparison.OrdinalIgnoreCase));

        if (duplicate)
        {
            return EventsErrors.Conflict("TierNameTaken", "Another tier on this date already has this name.");
        }

        return (name.Value!, description.Value, spec.Capacity);
    }

    private Result<(string Name, SalesWindow Window)> ValidatePhase(
        PhaseSpec spec,
        EventDate date,
        TicketTier tier,
        SalePhaseId? except,
        DateTimeOffset now)
    {
        var name = Text.Required(spec.Name, "PhaseName", SalePhase.NameMaxLength);
        if (name.IsFailure)
        {
            return name.Error!;
        }

        var price = ValidatePrice(spec.Price);
        if (price.IsFailure)
        {
            return price.Error!;
        }

        var window = ValidateWindow(spec.StartsAt, spec.EndsAt, date, now);
        if (window.IsFailure)
        {
            return window.Error!;
        }

        if (spec.Allocation is { } allocation && (allocation < 1 || allocation > tier.Capacity))
        {
            return EventsErrors.Invalid("Allocation", "A phase allocation must be between 1 and the tier capacity.");
        }

        if (tier.PhaseOverlaps(window.Value, except))
        {
            return EventsErrors.Conflict(
                "PhaseOverlaps",
                "Sale phases of one tier cannot overlap; a tier has one price at a time.");
        }

        var duplicate = tier.Phases.Any(x =>
            x.Id != except && string.Equals(x.Name, name.Value, StringComparison.OrdinalIgnoreCase));

        if (duplicate)
        {
            return EventsErrors.Conflict("PhaseNameTaken", "Another phase of this tier already has this name.");
        }

        return (name.Value!, window.Value);
    }

    private Result<(string Name, SalesWindow Window)> ValidateOffer(
        OfferSpec spec,
        GroupOfferKind kind,
        EventDate date,
        TicketTier tier,
        GroupOfferId? except,
        DateTimeOffset now)
    {
        if (!Enum.IsDefined(kind))
        {
            return EventsErrors.Invalid("OfferKind", "Unknown group offer kind.");
        }

        var name = Text.Required(spec.Name, "OfferName", GroupOffer.NameMaxLength);
        if (name.IsFailure)
        {
            return name.Error!;
        }

        var admitsValid = kind switch
        {
            GroupOfferKind.Couple => spec.Admits == GroupOffer.CoupleAdmits,
            GroupOfferKind.Gang => spec.Admits is >= GroupOffer.MinGangAdmits and <= GroupOffer.MaxGangAdmits,
            _ => false
        };

        if (!admitsValid)
        {
            return EventsErrors.Invalid(
                "Admits",
                kind == GroupOfferKind.Couple
                    ? $"A Couple offer admits exactly {GroupOffer.CoupleAdmits}."
                    : $"A Gang offer admits between {GroupOffer.MinGangAdmits} and {GroupOffer.MaxGangAdmits}.");
        }

        if (spec.Admits > tier.Capacity)
        {
            return EventsErrors.Invalid("Admits", "An offer cannot admit more people than the tier holds.");
        }

        var price = ValidatePrice(spec.Price);
        if (price.IsFailure)
        {
            return price.Error!;
        }

        var window = ValidateWindow(spec.StartsAt, spec.EndsAt, date, now);
        if (window.IsFailure)
        {
            return window.Error!;
        }

        if (spec.Allocation is { } allocation &&
            (allocation < 1 || (long)allocation * spec.Admits > tier.Capacity))
        {
            return EventsErrors.Invalid(
                "Allocation",
                "An offer allocation must be at least 1 and its admissions must fit the tier capacity.");
        }

        var duplicate = tier.Offers.Any(x =>
            x.Id != except && string.Equals(x.Name, name.Value, StringComparison.OrdinalIgnoreCase));

        if (duplicate)
        {
            return EventsErrors.Conflict("OfferNameTaken", "Another offer of this tier already has this name.");
        }

        return (name.Value!, window.Value);
    }

    private Result<SalesWindow> ValidateWindow(
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        EventDate date,
        DateTimeOffset now)
    {
        var window = SalesWindow.Create(startsAt, endsAt);
        if (window.IsFailure)
        {
            return window.Error!;
        }

        if (window.Value.EndsAt > date.EndsAt)
        {
            return EventsErrors.Invalid("SalesWindow", "Sales must end by the time the date ends.");
        }

        if (Status == EventStatus.Published && window.Value.HasEnded(now))
        {
            return EventsErrors.Invalid("SalesWindow", "On a published event, sales must end in the future.");
        }

        return window.Value;
    }

    private static Result ValidatePrice(Money price) =>
        price.IsPositive && price.IsWholeUnits
            ? Result.Success
            : EventsErrors.Invalid("Price", "Prices must be a positive whole number of shillings.");

    private static bool AllocationsExceed(TicketTier tier, int capacity) =>
        tier.Phases.Any(x => x.Allocation > capacity) ||
        tier.Offers.Any(x => x.Allocation is { } bundles && (long)bundles * x.Admits > capacity);

    private static string DescribeDate(EventDate date) =>
        date.Label is { } label
            ? $"'{label}'"
            : $"the date starting {date.StartsAt:yyyy-MM-dd HH:mm} UTC";

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex SlugPattern();
}
