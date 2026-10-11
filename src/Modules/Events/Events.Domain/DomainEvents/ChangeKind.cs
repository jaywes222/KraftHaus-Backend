namespace Events.Domain.DomainEvents;

public enum ChangeKind
{
    Added = 1,
    Updated = 2,
    Removed = 3,

    /// <summary>Taken off sale after publishing (kept for history; Ticketing may already reference it).</summary>
    Withdrawn = 4,

    Cancelled = 5
}

/// <summary>The part of an event's structure a <see cref="EventStructureChanged" /> concerns.</summary>
public enum EventPart
{
    Date = 1,
    Tier = 2,
    SalePhase = 3,
    GroupOffer = 4,
    Entrance = 5
}
