using Common.Domain.ResultMonad;
using EntityFramework.Exceptions.Common;
using Microsoft.EntityFrameworkCore;
using Music.Application.Abstractions;
using Music.Domain.Common;
using Music.Domain.Requests;
using Music.Infrastructure.Persistence.Configurations;

namespace Music.Infrastructure.Persistence.Repositories;

internal sealed class RequestBoardRepository(MusicDbContext db) : IRequestBoardRepository
{
    public Task<RequestBoard?> FindForEventAsync(Guid eventId, CancellationToken cancellationToken) =>
        db.Boards.SingleOrDefaultAsync(x => x.EventId == eventId, cancellationToken);

    public void Add(RequestBoard board) => db.Boards.Add(board);
}

internal sealed class SongRequestRepository(MusicDbContext db) : ISongRequestRepository
{
    public Task<SongRequest?> FindAsync(SongRequestId id, CancellationToken cancellationToken) =>
        db.Requests.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<SongRequest?> FindOpenByKeyAsync(Guid eventId, string trackKey, CancellationToken cancellationToken) =>
        db.Requests.SingleOrDefaultAsync(
            x => x.EventId == eventId &&
                 x.TrackKey == trackKey &&
                 (x.Status == SongRequestStatus.Waiting || x.Status == SongRequestStatus.UpNext),
            cancellationToken);

    public async Task<IReadOnlyList<SongRequest>> ListForEventAsync(Guid eventId, CancellationToken cancellationToken) =>
        await db.Requests
            .AsNoTracking()
            .Where(x => x.EventId == eventId)
            .OrderByDescending(x => x.LastVotedAt)
            .ToListAsync(cancellationToken);

    public Task<int> CountVotesAsync(Guid eventId, Guid userId, CancellationToken cancellationToken) =>
        db.Requests
            .Where(x => x.EventId == eventId)
            .SelectMany(x => x.Votes)
            .CountAsync(v => v.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<Guid>> ListVotedRequestIdsAsync(Guid eventId, Guid userId, CancellationToken cancellationToken)
    {
        var ids = await db.Requests
            .Where(x => x.EventId == eventId && x.Votes.Any(v => v.UserId == userId))
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        return [.. ids.Select(x => x.Value)];
    }

    public void Add(SongRequest request) => db.Requests.Add(request);
}

internal sealed class MusicUnitOfWork(MusicDbContext db) : IMusicUnitOfWork
{
    public async Task<Result> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success;
        }
        catch (DbUpdateConcurrencyException)
        {
            return MusicErrors.ConcurrentUpdate;
        }
        catch (UniqueConstraintException ex) when (ex.ConstraintName == SongRequestConfiguration.OpenRequestIndex)
        {
            return MusicUnitOfWorkErrors.DuplicateOpenRequest;
        }
        catch (UniqueConstraintException)
        {
            // The board (one per event) or a person's vote (one per request) already exists: a double tap.
            return MusicErrors.Conflict("AlreadyExists", "That was just done. Refresh and try again.");
        }
    }

    public void Reset() => db.ChangeTracker.Clear();
}
