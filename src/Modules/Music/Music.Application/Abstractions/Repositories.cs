using Common.Domain.ResultMonad;
using Music.Domain.Common;
using Music.Domain.Requests;

namespace Music.Application.Abstractions;

public interface IRequestBoardRepository
{
    Task<RequestBoard?> FindForEventAsync(Guid eventId, CancellationToken cancellationToken);

    void Add(RequestBoard board);
}

public interface ISongRequestRepository
{
    Task<SongRequest?> FindAsync(SongRequestId id, CancellationToken cancellationToken);

    /// <summary>The open (Waiting or UpNext) request for this song at this event, if any.</summary>
    Task<SongRequest?> FindOpenByKeyAsync(Guid eventId, string trackKey, CancellationToken cancellationToken);

    /// <summary>Every request of the event (with votes), newest activity first.</summary>
    Task<IReadOnlyList<SongRequest>> ListForEventAsync(Guid eventId, CancellationToken cancellationToken);

    /// <summary>How many requests (votes) this person has made for the event, whatever happened to them since.</summary>
    Task<int> CountVotesAsync(Guid eventId, Guid userId, CancellationToken cancellationToken);

    /// <summary>The requests this person voted for at this event.</summary>
    Task<IReadOnlyList<Guid>> ListVotedRequestIdsAsync(Guid eventId, Guid userId, CancellationToken cancellationToken);

    void Add(SongRequest request);
}

public interface IMusicUnitOfWork
{
    /// <summary>
    ///     Saves. A lost optimistic-concurrency race comes back as <see cref="MusicErrors.ConcurrentUpdate" />; a
    ///     second open request for the same song (two fans at the same moment) as <see cref="MusicUnitOfWorkErrors.DuplicateOpenRequest" />.
    /// </summary>
    Task<Result> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Forgets every tracked change (after a failed save, before trying again).</summary>
    void Reset();
}

public static class MusicUnitOfWorkErrors
{
    public const string DuplicateOpenRequestKey = "Music.DuplicateOpenRequest";

    public static Error DuplicateOpenRequest =>
        MusicErrors.Conflict("DuplicateOpenRequest", "Someone asked for this song at the same moment. Try again.");
}
