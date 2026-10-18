using Common.Application.Music;
using Music.Application.Abstractions;
using Music.Domain.Requests;

namespace Music.Application.Requests;

/// <summary><see cref="IMusicBoard" /> for the DJ booth slide on screens.</summary>
public sealed class MusicBoardProvider(IRequestBoardRepository boards, ISongRequestRepository requests) : IMusicBoard
{
    public const int JustPlayedShown = 3;

    public async Task<MusicBoard> GetAsync(Guid eventId, int queueCount, CancellationToken cancellationToken)
    {
        var board = await boards.FindForEventAsync(eventId, cancellationToken);
        var list = await requests.ListForEventAsync(eventId, cancellationToken);

        var upNext = list.Where(x => x.Status == SongRequestStatus.UpNext).OrderBy(x => x.HandledAt ?? x.RequestedAt).FirstOrDefault();
        var queue = list
            .Where(x => x.Status == SongRequestStatus.Waiting)
            .OrderByDescending(x => x.VoteCount)
            .ThenBy(x => x.RequestedAt)
            .Take(Math.Clamp(queueCount, 1, 20));
        var played = list
            .Where(x => x.Status == SongRequestStatus.Played)
            .OrderByDescending(x => x.HandledAt)
            .Take(JustPlayedShown);

        return new MusicBoard(
            board?.IsOpen ?? true,
            upNext is null ? null : Track(upNext),
            [.. queue.Select(Track)],
            [.. played.Select(Track)]);
    }

    private static MusicBoardTrack Track(SongRequest request) =>
        new(request.Track.Title, request.Track.Artist, CoverArt.Url(request.Track.ReleaseId), request.VoteCount);
}
