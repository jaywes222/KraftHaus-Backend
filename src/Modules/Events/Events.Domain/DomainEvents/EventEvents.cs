using Common.Domain.Events;
using Events.Domain.Common;

namespace Events.Domain.DomainEvents;

public sealed record EventCreated(EventId EventId, string Slug, string Title) : DomainEvent;

public sealed record EventDetailsUpdated(EventId EventId, string Slug, string Title) : DomainEvent;

/// <summary>The event's venue copy was set from the library (<paramref name="SourceVenueId" />) or typed inline.</summary>
public sealed record EventVenueSet(EventId EventId, VenueId? SourceVenueId, string VenueName) : DomainEvent;

public sealed record EventVenueUpdated(EventId EventId, string VenueName) : DomainEvent;

public sealed record EventPoliciesUpdated(
    EventId EventId,
    bool RefundsEnabled,
    bool TransfersEnabled,
    int MaxTransfersPerTicket,
    bool LipaMdogoMdogoEnabled,
    int HoldMinutes,
    int MaxTicketsPerOrder) : DomainEvent;

/// <summary>
///     A date, tier, sale phase, group offer or entrance of the event was added, changed, removed, withdrawn or
///     cancelled. <paramref name="ItemId" /> is the id of that part.
/// </summary>
public sealed record EventStructureChanged(
    EventId EventId,
    EventPart Part,
    ChangeKind Change,
    Guid ItemId) : DomainEvent;

public sealed record EventPublished(EventId EventId, DateTimeOffset PublishedAt) : DomainEvent;

public sealed record EventCancelled(EventId EventId, string Reason) : DomainEvent;

public sealed record EventCompleted(EventId EventId) : DomainEvent;
