using Common.Domain.Entities;
using Events.Domain.Common;

namespace Events.Domain.Model;

/// <summary>An entrance of this event's venue copy. Check-in can later record which gate a ticket used.</summary>
public sealed class EventEntrance : AuditableEntity<EventEntranceId>
{
    private EventEntrance(EventEntranceId id)
        : base(id)
    {
    }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    internal static EventEntrance Create(EventEntranceId id, string name, string? description) =>
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
