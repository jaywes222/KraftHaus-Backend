using Common.Domain.Aggregates;
using Common.Domain.ResultMonad;
using Events.Domain.Common;
using Events.Domain.DomainEvents;

namespace Events.Domain.Venues;

/// <summary>
///     A reusable venue in the tenant's library. An event copies it when the venue is attached and from then on
///     edits its own copy (Spec 08 §4 Events: "venues, entrances").
/// </summary>
public sealed class Venue : AggregateRoot<VenueId>
{
    public const int MaxCapacity = 1_000_000;

    private readonly List<VenueEntrance> _entrances = [];

    private Venue(VenueId id)
        : base(id)
    {
    }

    public VenueDetails Details { get; private set; } = null!;

    /// <summary>Optional physical capacity, informational only (ticket capacity lives on each tier).</summary>
    public int? Capacity { get; private set; }

    public VenueStatus Status { get; private set; }

    public IReadOnlyList<VenueEntrance> Entrances => _entrances.AsReadOnly();

    public static Result<Venue> Create(VenueId id, VenueDetails details, int? capacity)
    {
        ArgumentNullException.ThrowIfNull(details);

        var validCapacity = ValidateCapacity(capacity);
        if (validCapacity.IsFailure)
        {
            return validCapacity.Error!;
        }

        var venue = new Venue(id)
        {
            Details = details,
            Capacity = capacity,
            Status = VenueStatus.Active
        };

        venue.RaiseEvent(new VenueCreated(id, details.Name));

        return venue;
    }

    public Result Update(VenueDetails details, int? capacity)
    {
        ArgumentNullException.ThrowIfNull(details);

        var active = EnsureActive();
        if (active.IsFailure)
        {
            return active;
        }

        var validCapacity = ValidateCapacity(capacity);
        if (validCapacity.IsFailure)
        {
            return validCapacity;
        }

        var @event = new VenueUpdated(Id, details.Name);

        Details = details;
        Capacity = capacity;

        RaiseEvent(@event);

        return Result.Success;
    }

    public Result<VenueEntrance> AddEntrance(VenueEntranceId entranceId, string? name, string? description)
    {
        var active = EnsureActive();
        if (active.IsFailure)
        {
            return active.Error!;
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
                $"A venue can have at most {EntranceRules.MaxEntrances} entrances.");
        }

        if (_entrances.Exists(x => string.Equals(x.Name, validName, StringComparison.OrdinalIgnoreCase)))
        {
            return EventsErrors.Conflict("EntranceNameTaken", "Another entrance already has this name.");
        }

        var @event = new VenueEntranceChanged(Id, entranceId, ChangeKind.Added);

        var entrance = VenueEntrance.Create(entranceId, validName, validDescription);
        _entrances.Add(entrance);

        RaiseEvent(@event);

        return entrance;
    }

    public Result UpdateEntrance(VenueEntranceId entranceId, string? name, string? description)
    {
        var active = EnsureActive();
        if (active.IsFailure)
        {
            return active;
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

        var @event = new VenueEntranceChanged(Id, entranceId, ChangeKind.Updated);

        entrance.Update(validName, validDescription);

        RaiseEvent(@event);

        return Result.Success;
    }

    public Result RemoveEntrance(VenueEntranceId entranceId)
    {
        var active = EnsureActive();
        if (active.IsFailure)
        {
            return active;
        }

        var entrance = _entrances.Find(x => x.Id == entranceId);
        if (entrance is null)
        {
            return EventsErrors.NotFound("Entrance", entranceId.Value);
        }

        var @event = new VenueEntranceChanged(Id, entranceId, ChangeKind.Removed);

        _entrances.Remove(entrance);

        RaiseEvent(@event);

        return Result.Success;
    }

    public Result Archive()
    {
        if (Status == VenueStatus.Archived)
        {
            return EventsErrors.InvalidState("VenueArchived", "The venue is already archived.");
        }

        var @event = new VenueArchived(Id);

        Status = VenueStatus.Archived;

        RaiseEvent(@event);

        return Result.Success;
    }

    private Result EnsureActive() =>
        Status == VenueStatus.Archived
            ? EventsErrors.InvalidState("VenueArchived", "An archived venue cannot be changed.")
            : Result.Success;

    private static Result ValidateCapacity(int? capacity) =>
        capacity is <= 0 or > MaxCapacity
            ? EventsErrors.Invalid("Capacity", $"Capacity must be between 1 and {MaxCapacity}.")
            : Result.Success;
}
