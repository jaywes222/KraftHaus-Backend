using System.Globalization;
using Music.Application.Catalog;
using Music.Domain.Requests;

namespace Music.Application.Requests;

/// <summary>A song from the catalogue search, ready to request.</summary>
public sealed record CatalogTrackView(
    string Id,
    string Title,
    string Artist,
    string? Release,
    int? Year,
    int? DurationSeconds,
    Uri? CoverUrl)
{
    public static CatalogTrackView From(CatalogTrack track)
    {
        ArgumentNullException.ThrowIfNull(track);

        return new CatalogTrackView(track.Id, track.Title, track.Artist, track.Release, track.Year, track.DurationSeconds, CoverArt.Url(track.ReleaseId));
    }
}

/// <summary>What everyone sees on the event's request list: songs and how many asked, never who.</summary>
public sealed record PublicSongRequestView(
    Guid Id,
    string Title,
    string Artist,
    Uri? CoverUrl,
    int Votes,
    SongRequestStatus Status)
{
    public static PublicSongRequestView From(SongRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new PublicSongRequestView(
            request.Id.Value,
            request.Track.Title,
            request.Track.Artist,
            CoverArt.Url(request.Track.ReleaseId),
            request.VoteCount,
            request.Status);
    }
}

/// <param name="Queue">Open requests: Up next first, then the most asked-for, then the oldest.</param>
/// <param name="RecentlyPlayed">The last songs played from the list, newest first.</param>
public sealed record RequestBoardPublicView(
    bool Open,
    int MaxPerPerson,
    string? Note,
    IReadOnlyList<PublicSongRequestView> Queue,
    IReadOnlyList<PublicSongRequestView> RecentlyPlayed);

/// <summary>Why a signed-in person can or cannot request right now.</summary>
public enum RequestEligibility
{
    CanRequest = 1,
    RequestsClosed = 2,
    TicketRequired = 3,
    PhoneRequired = 4,
    LimitReached = 5
}

public sealed record MySongRequestsView(
    RequestEligibility Eligibility,
    int Used,
    int MaxPerPerson,
    IReadOnlyList<Guid> RequestIds);

/// <param name="Seconded">True when the song was already on the list and this request added a vote to it.</param>
public sealed record SongRequestResultView(
    PublicSongRequestView Request,
    bool Seconded,
    int Used,
    int MaxPerPerson);

/// <summary>A request in the DJ console: the song, the votes and the first names behind them (for shout-outs).</summary>
public sealed record DjSongRequestView(
    Guid Id,
    string Title,
    string Artist,
    string? Release,
    int? Year,
    int? DurationSeconds,
    Uri? CoverUrl,
    TrackSource Source,
    SongRequestStatus Status,
    int Votes,
    IReadOnlyList<string> RequestedBy,
    DateTimeOffset RequestedAt,
    DateTimeOffset LastVotedAt,
    DateTimeOffset? HandledAt,
    long Version)
{
    /// <summary>Shout-out names shown per request; the rest are counted in <see cref="Votes" />.</summary>
    public const int NamesShown = 5;

    public static DjSongRequestView From(SongRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new DjSongRequestView(
            request.Id.Value,
            request.Track.Title,
            request.Track.Artist,
            request.Track.Release,
            request.Track.Year,
            request.Track.DurationSeconds,
            CoverArt.Url(request.Track.ReleaseId),
            request.Track.Source,
            request.Status,
            request.VoteCount,
            [.. request.Votes.OrderBy(x => x.VotedAt).Take(NamesShown).Select(x => FirstName(x.Name))],
            request.RequestedAt,
            request.LastVotedAt,
            request.HandledAt,
            request.Version);
    }

    private static string FirstName(string name)
    {
        var first = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

        return string.IsNullOrEmpty(first) ? "A fan" : first;
    }
}

public sealed record RequestBoardView(Guid EventId, bool Open, int MaxPerPerson, string? Note, DateTimeOffset? OpenedAt, DateTimeOffset? ClosedAt)
{
    public static RequestBoardView From(RequestBoard board)
    {
        ArgumentNullException.ThrowIfNull(board);

        return new RequestBoardView(board.EventId, board.IsOpen, board.MaxPerPerson, board.Note, board.OpenedAt, board.ClosedAt);
    }
}

public sealed record DjConsoleView(RequestBoardView Board, IReadOnlyList<DjSongRequestView> Requests);

public sealed record ConfigureBoardRequest(bool Open, int MaxPerPerson, string? Note);

public sealed record MoveSongRequestRequest(SongRequestStatus Status);

/// <summary>Either a catalogue song (<see cref="TrackId" />) or a typed one (<see cref="Title" /> and <see cref="Artist" />).</summary>
public sealed record CreateSongRequestRequest(string? TrackId, string? Title, string? Artist);

/// <summary>Cover images come straight from the Cover Art Archive (MusicBrainz's sister project); missing art is normal.</summary>
public static class CoverArt
{
    public const string Host = "https://coverartarchive.org";

    public static Uri? Url(string? releaseId) =>
        Guid.TryParse(releaseId, out var id)
            ? new Uri(string.Create(CultureInfo.InvariantCulture, $"{Host}/release/{id:D}/front-250"))
            : null;
}
