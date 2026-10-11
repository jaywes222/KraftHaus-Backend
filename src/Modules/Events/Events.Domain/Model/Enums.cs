namespace Events.Domain.Model;

/// <summary>
///     Event lifecycle. <c>Draft → Published → Completed</c>; <c>Cancelled</c> from Draft or Published. Whether a
///     tier is on sale at a given moment is derived from its sale phases' windows, not from a separate status.
/// </summary>
public enum EventStatus
{
    Draft = 1,
    Published = 2,
    Cancelled = 3,
    Completed = 4
}

public enum EventDateStatus
{
    Scheduled = 1,
    Cancelled = 2
}

/// <summary>Shared by tiers and group offers.</summary>
public enum SaleItemStatus
{
    Active = 1,

    /// <summary>Taken off sale after publishing. Kept because tickets may already have been sold against it.</summary>
    Withdrawn = 2
}

public enum GroupOfferKind
{
    /// <summary>Exactly two admissions for one price.</summary>
    Couple = 1,

    /// <summary>Three or more admissions for one price ("Gang Gang").</summary>
    Gang = 2
}
