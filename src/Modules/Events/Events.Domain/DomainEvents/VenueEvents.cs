using Common.Domain.Events;
using Events.Domain.Common;

namespace Events.Domain.DomainEvents;

public sealed record VenueCreated(VenueId VenueId, string Name) : DomainEvent;

public sealed record VenueUpdated(VenueId VenueId, string Name) : DomainEvent;

public sealed record VenueEntranceChanged(VenueId VenueId, VenueEntranceId EntranceId, ChangeKind Change) : DomainEvent;

public sealed record VenueArchived(VenueId VenueId) : DomainEvent;
