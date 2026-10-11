using Events.Application.Abstractions;
using Events.Domain.Common;
using Events.Domain.Venues;
using Microsoft.EntityFrameworkCore;

namespace Events.Infrastructure.Persistence.Repositories;

internal sealed class VenueRepository(EventsDbContext db) : IVenueRepository
{
    public Task<Venue?> FindAsync(VenueId id, CancellationToken cancellationToken) =>
        db.Venues
            .TagWith(nameof(FindAsync))
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<Venue?> FindForReadAsync(VenueId id, CancellationToken cancellationToken) =>
        db.Venues
            .AsNoTracking()
            .TagWith(nameof(FindForReadAsync))
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Venue>> ListForReadAsync(bool includeArchived, CancellationToken cancellationToken) =>
        await db.Venues
            .AsNoTracking()
            .TagWith(nameof(ListForReadAsync))
            .Where(x => includeArchived || x.Status == VenueStatus.Active)
            .OrderBy(x => x.Details.Name)
            .ToListAsync(cancellationToken);

    public void Add(Venue venue) => db.Venues.Add(venue);
}

internal sealed class EventsUnitOfWork(EventsDbContext db) : IEventsUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
