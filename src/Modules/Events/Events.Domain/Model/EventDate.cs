using Common.Domain.Entities;
using Events.Domain.Common;

namespace Events.Domain.Model;

/// <summary>One day (or night) of an event. Each date has its own tiers, prices and capacity.</summary>
public sealed class EventDate : AuditableEntity<EventDateId>
{
    public const int LabelMaxLength = 80;

    private readonly List<TicketTier> _tiers = [];

    private EventDate(EventDateId id)
        : base(id)
    {
    }

    /// <summary>Optional label such as "Day 1" or "Friday night".</summary>
    public string? Label { get; private set; }

    public DateTimeOffset StartsAt { get; private set; }

    public DateTimeOffset EndsAt { get; private set; }

    public DateTimeOffset? DoorsOpenAt { get; private set; }

    public EventDateStatus Status { get; private set; }

    public IReadOnlyList<TicketTier> Tiers => _tiers.AsReadOnly();

    internal static EventDate Create(
        EventDateId id,
        string? label,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        DateTimeOffset? doorsOpenAt) =>
        new(id)
        {
            Label = label,
            StartsAt = startsAt,
            EndsAt = endsAt,
            DoorsOpenAt = doorsOpenAt,
            Status = EventDateStatus.Scheduled
        };

    internal void Update(string? label, DateTimeOffset startsAt, DateTimeOffset endsAt, DateTimeOffset? doorsOpenAt)
    {
        Label = label;
        StartsAt = startsAt;
        EndsAt = endsAt;
        DoorsOpenAt = doorsOpenAt;
    }

    internal void Cancel() => Status = EventDateStatus.Cancelled;

    internal TicketTier? FindTier(TicketTierId tierId) => _tiers.Find(x => x.Id == tierId);

    internal int NextTierPosition => _tiers.Count == 0 ? 1 : _tiers.Max(x => x.Position) + 1;

    internal void AddTier(TicketTier tier) => _tiers.Add(tier);

    internal void RemoveTier(TicketTier tier) => _tiers.Remove(tier);
}
