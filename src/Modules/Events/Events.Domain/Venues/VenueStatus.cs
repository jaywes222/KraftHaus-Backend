namespace Events.Domain.Venues;

public enum VenueStatus
{
    Active = 1,

    /// <summary>Hidden from the venue picker. Events that already copied it are unaffected.</summary>
    Archived = 2
}
