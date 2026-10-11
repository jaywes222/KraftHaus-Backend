using Events.Domain.Common;
using Events.Domain.Model;
using Events.Domain.Venues;

namespace Events.Application.Abstractions;

/// <summary>Event aggregates in the resolved tenant's database.</summary>
public interface IEventRepository
{
    /// <summary>Loads the whole aggregate (dates, tiers, phases, offers, entrances) for change tracking.</summary>
    Task<Event?> FindAsync(EventId id, CancellationToken cancellationToken);

    /// <summary>Loads the whole aggregate without tracking, for reads.</summary>
    Task<Event?> FindForReadAsync(EventId id, CancellationToken cancellationToken);

    Task<Event?> FindBySlugForReadAsync(string slug, CancellationToken cancellationToken);

    Task<bool> SlugExistsAsync(string slug, EventId? except, CancellationToken cancellationToken);

    /// <summary>Whole aggregates without tracking, newest first, optionally filtered by status.</summary>
    Task<IReadOnlyList<Event>> ListForReadAsync(EventStatus? status, CancellationToken cancellationToken);

    /// <summary>Published events with at least one scheduled date that has not ended at <paramref name="now" />.</summary>
    Task<IReadOnlyList<Event>> ListUpcomingPublishedAsync(DateTimeOffset now, CancellationToken cancellationToken);

    void Add(Event @event);
}

/// <summary>Reusable venues in the resolved tenant's database.</summary>
public interface IVenueRepository
{
    Task<Venue?> FindAsync(VenueId id, CancellationToken cancellationToken);

    Task<Venue?> FindForReadAsync(VenueId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Venue>> ListForReadAsync(bool includeArchived, CancellationToken cancellationToken);

    void Add(Venue venue);
}

public interface IEventsUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
