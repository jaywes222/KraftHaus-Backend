using Common.Domain.Entities;
using Events.Domain.Common;

namespace Events.Domain.Venues;

/// <summary>A gate of a reusable venue (e.g. "Main gate", "VIP entrance"). Copied onto events that use the venue.</summary>
public sealed class VenueEntrance : AuditableEntity<VenueEntranceId>
{
    private VenueEntrance(VenueEntranceId id)
        : base(id)
    {
    }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    internal static VenueEntrance Create(VenueEntranceId id, string name, string? description) =>
        new(id)
        {
            Name = name,
            Description = description
        };

    internal void Update(string name, string? description)
    {
        Name = name;
        Description = description;
    }
}
