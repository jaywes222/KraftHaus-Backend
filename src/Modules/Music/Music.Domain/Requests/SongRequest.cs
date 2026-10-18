using Common.Domain.Aggregates;
using Common.Domain.Entities;
using Common.Domain.ResultMonad;
using Music.Domain.Common;

namespace Music.Domain.Requests;

public enum SongRequestStatus
{
    /// <summary>On the list, not yet picked.</summary>
    Waiting = 1,

    /// <summary>The DJ lined it up.</summary>
    UpNext = 2,

    Played = 3,

    /// <summary>"Not tonight": the DJ passed on it.</summary>
    Declined = 4
}

/// <summary>One fan's request for a song (a vote). The first one creates the request; later ones second it.</summary>
public sealed class SongRequestVote : AuditableEntity<SongRequestVoteId>
{
    public const int NameMaxLength = 80;

    private SongRequestVote(SongRequestVoteId id)
        : base(id)
    {
    }

    public Guid UserId { get; private set; }

    /// <summary>The account's display name, so the DJ can give a shout-out.</summary>
    public string Name { get; private set; } = string.Empty;

    public DateTimeOffset VotedAt { get; private set; }

    internal static SongRequestVote Create(SongRequestVoteId id, Guid userId, string name, DateTimeOffset now) =>
        new(id)
        {
            UserId = userId,
            Name = name.Length <= NameMaxLength ? name : name[..NameMaxLength],
            VotedAt = now
        };
}

/// <summary>
///     A song on an event's request list (UF: song requests). Several fans asking for the same song add votes to one
///     request while it is open (Waiting or UpNext); the DJ moves it to Played or Declined.
/// </summary>
public sealed class SongRequest : AggregateRoot<SongRequestId>
{
    private readonly List<SongRequestVote> _votes = [];

    private SongRequest(SongRequestId id)
        : base(id)
    {
    }

    public Guid EventId { get; private set; }

    public Track Track { get; private set; } = null!;

    /// <summary>Copy of <see cref="Track.Key" /> as a column, for the "one open request per song" index.</summary>
    public string TrackKey { get; private set; } = string.Empty;

    public SongRequestStatus Status { get; private set; }

    public DateTimeOffset RequestedAt { get; private set; }

    public DateTimeOffset LastVotedAt { get; private set; }

    public int VoteCount { get; private set; }

    public DateTimeOffset? HandledAt { get; private set; }

    public Guid? HandledBy { get; private set; }

    public IReadOnlyList<SongRequestVote> Votes => _votes.AsReadOnly();

    public bool IsOpen => Status is SongRequestStatus.Waiting or SongRequestStatus.UpNext;

    public static SongRequest Create(
        SongRequestId id,
        Guid eventId,
        Track track,
        SongRequestVoteId voteId,
        Guid userId,
        string name,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(track);

        var request = new SongRequest(id)
        {
            EventId = eventId,
            Track = track,
            TrackKey = track.Key,
            Status = SongRequestStatus.Waiting,
            RequestedAt = now,
            LastVotedAt = now,
            VoteCount = 1
        };

        request._votes.Add(SongRequestVote.Create(voteId, userId, name, now));
        request.RaiseEvent(new SongRequested(id, eventId, track.Source));

        return request;
    }

    public bool HasVoteFrom(Guid userId) => _votes.Exists(x => x.UserId == userId);

    /// <summary>Another fan asks for the same song.</summary>
    public Result Second(SongRequestVoteId voteId, Guid userId, string name, DateTimeOffset now)
    {
        if (!IsOpen)
        {
            return MusicErrors.Conflict("RequestClosed", "This request was already played or passed on.");
        }

        if (HasVoteFrom(userId))
        {
            return MusicErrors.AlreadyRequested;
        }

        var @event = new SongRequestSeconded(Id, EventId, VoteCount + 1);

        _votes.Add(SongRequestVote.Create(voteId, userId, name, now));
        VoteCount += 1;
        LastVotedAt = now;
        RaiseEvent(@event);

        return Result.Success;
    }

    /// <summary>
    ///     The DJ's moves: Waiting ⇄ UpNext, either of them → Played or Declined. Played and Declined are final, except
    ///     that a misclick can be taken back to Waiting within the same night (the console's "undo").
    /// </summary>
    public Result MoveTo(SongRequestStatus status, Guid staffUserId, DateTimeOffset now)
    {
        if (!Enum.IsDefined(status))
        {
            return MusicErrors.Invalid("StatusInvalid", ["Status: Use Waiting, UpNext, Played or Declined."]);
        }

        if (status == Status)
        {
            return Result.Success;
        }

        var allowed = (Status, status) switch
        {
            (SongRequestStatus.Waiting, _) => true,
            (SongRequestStatus.UpNext, _) => true,
            (SongRequestStatus.Played or SongRequestStatus.Declined, SongRequestStatus.Waiting) => true,
            _ => false
        };

        if (!allowed)
        {
            return MusicErrors.Conflict("StatusChangeNotAllowed", $"A request that is {Status} can only go back to Waiting.");
        }

        var @event = new SongRequestHandled(Id, EventId, Status, status);

        Status = status;
        HandledAt = status == SongRequestStatus.Waiting ? null : now;
        HandledBy = status == SongRequestStatus.Waiting ? null : staffUserId;
        RaiseEvent(@event);

        return Result.Success;
    }
}
