namespace Common.Application.Music;

/// <summary>The DJ booth for a screen (implemented by Music): what's next and what fans are asking for. No names.</summary>
public interface IMusicBoard
{
    Task<MusicBoard> GetAsync(Guid eventId, int queueCount, CancellationToken cancellationToken);
}

/// <param name="UpNext">The song the DJ marked up next, if any.</param>
/// <param name="Queue">The most asked-for open requests.</param>
/// <param name="JustPlayed">The last songs played from the list, newest first.</param>
public sealed record MusicBoard(bool Open, MusicBoardTrack? UpNext, IReadOnlyList<MusicBoardTrack> Queue, IReadOnlyList<MusicBoardTrack> JustPlayed);

public sealed record MusicBoardTrack(string Title, string Artist, Uri? CoverUrl, int Votes);
