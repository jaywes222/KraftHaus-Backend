using Events.Application.Abstractions;
using Events.Domain.Common;
using Events.Domain.Model;
using Microsoft.EntityFrameworkCore;

namespace Events.Infrastructure.Persistence.Repositories;

internal sealed class EventRepository(EventsDbContext db) : IEventRepository
{
    public Task<Event?> FindAsync(EventId id, CancellationToken cancellationToken) =>
        WithGraph(db.Events)
            .TagWith(nameof(FindAsync))
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<Event?> FindForReadAsync(EventId id, CancellationToken cancellationToken) =>
        WithGraph(db.Events.AsNoTracking())
            .TagWith(nameof(FindForReadAsync))
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<Event?> FindBySlugForReadAsync(string slug, CancellationToken cancellationToken) =>
        WithGraph(db.Events.AsNoTracking())
            .TagWith(nameof(FindBySlugForReadAsync))
            .SingleOrDefaultAsync(x => x.Slug == slug, cancellationToken);

    public Task<bool> SlugExistsAsync(string slug, EventId? except, CancellationToken cancellationToken) =>
        db.Events
            .AsNoTracking()
            .TagWith(nameof(SlugExistsAsync))
            .AnyAsync(x => x.Slug == slug && (except == null || x.Id != except), cancellationToken);

    public async Task<IReadOnlyList<Event>> ListForReadAsync(EventStatus? status, CancellationToken cancellationToken)
    {
        var query = WithGraph(db.Events.AsNoTracking()).TagWith(nameof(ListForReadAsync));

        if (status is { } filter)
        {
            query = query.Where(x => x.Status == filter);
        }

        return await query
            .OrderByDescending(x => x.Id)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Event>> ListUpcomingPublishedAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        await WithGraph(db.Events.AsNoTracking())
            .TagWith(nameof(ListUpcomingPublishedAsync))
            .Where(x => x.Status == EventStatus.Published &&
                        x.Dates.Any(d => d.Status == EventDateStatus.Scheduled && d.EndsAt > now))
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

    public void Add(Event @event) => db.Events.Add(@event);

    private static IQueryable<Event> WithGraph(IQueryable<Event> query) =>
        query
            .Include(x => x.Entrances)
            .Include(x => x.Dates)
            .ThenInclude(x => x.Tiers)
            .ThenInclude(x => x.Phases)
            .Include(x => x.Dates)
            .ThenInclude(x => x.Tiers)
            .ThenInclude(x => x.Offers);
}
