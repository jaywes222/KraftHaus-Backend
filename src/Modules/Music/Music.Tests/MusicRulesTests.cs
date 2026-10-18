using Music.Domain.Common;
using Music.Domain.Requests;
using Xunit;

namespace Music.Tests;

public sealed class MusicRulesTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TypedSongsMatchWhateverTheCaseAccentsAndPunctuation()
    {
        var a = Track.Typed("Suzanna", "Sauti Sol").Value!;
        var b = Track.Typed("  SUZANNA!! ", "sauti   sol").Value!;
        var c = Track.Typed("Crazy in Love", "Beyoncé").Value!;
        var d = Track.Typed("crazy in love", "Beyonce").Value!;

        Assert.Equal(a.Key, b.Key);
        Assert.Equal(c.Key, d.Key);
        Assert.NotEqual(a.Key, c.Key);
        Assert.Equal(TrackSource.Typed, a.Source);
        Assert.Equal("SUZANNA!!", b.Title);
    }

    [Theory]
    [InlineData("", "Sauti Sol")]
    [InlineData("S", "Sauti Sol")]
    [InlineData("Suzanna", " ")]
    public void TypedSongsNeedATitleAndAnArtist(string title, string artist)
    {
        var result = Track.Typed(title, artist);

        Assert.True(result.IsFailure);
        Assert.Equal("Music.RequestInvalid", result.Error!.Key);
    }

    [Fact]
    public void CatalogSongsAreKeyedByTheirRecordingId()
    {
        var id = Guid.NewGuid();
        var track = Track.FromCatalog(id.ToString("D").ToUpperInvariant(), "Halo", "Beyoncé", "I Am… Sasha Fierce", "not-a-guid", 2008, 261).Value!;

        Assert.Equal($"mb:{id:D}", track.Key);
        Assert.Null(track.ReleaseId);
        Assert.Equal(2008, track.Year);

        Assert.Equal("Music.TrackNotFound", Track.FromCatalog("nope", "Halo", "Beyoncé", null, null, null, null).Error!.Key);
    }

    [Fact]
    public void AnotherFanAddsAVoteButNotTwice()
    {
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var request = NewRequest(alice);

        Assert.False(request.Second(new SongRequestVoteId(Guid.NewGuid()), bob, "Bob Otieno", Now).IsFailure);
        Assert.Equal(2, request.VoteCount);

        var again = request.Second(new SongRequestVoteId(Guid.NewGuid()), alice, "Alice", Now);
        Assert.Equal("Music.AlreadyRequested", again.Error!.Key);
        Assert.Equal(2, request.VoteCount);
        Assert.Equal(2, request.Votes.Count);
        Assert.IsType<SongRequestSeconded>(request.Events.Last());
    }

    [Fact]
    public void ThePlayedSongCannotBeVotedForAndOnlyGoesBackToWaiting()
    {
        var staff = Guid.NewGuid();
        var request = NewRequest(Guid.NewGuid());

        Assert.False(request.MoveTo(SongRequestStatus.UpNext, staff, Now).IsFailure);
        Assert.False(request.MoveTo(SongRequestStatus.Played, staff, Now).IsFailure);
        Assert.Equal(Now, request.HandledAt);
        Assert.False(request.IsOpen);

        Assert.Equal("Music.RequestClosed", request.Second(new SongRequestVoteId(Guid.NewGuid()), Guid.NewGuid(), "Bob", Now).Error!.Key);
        Assert.Equal("Music.StatusChangeNotAllowed", request.MoveTo(SongRequestStatus.Declined, staff, Now).Error!.Key);

        // Undo a misclick.
        Assert.False(request.MoveTo(SongRequestStatus.Waiting, staff, Now).IsFailure);
        Assert.Null(request.HandledAt);
        Assert.True(request.IsOpen);
    }

    [Fact]
    public void TheBoardOpensClosesAndValidatesItsLimit()
    {
        var board = RequestBoard.Create(new RequestBoardId(Guid.NewGuid()), Guid.NewGuid(), 3);
        Assert.False(board.IsOpen);

        Assert.False(board.Configure(open: true, 5, "  DJ Kace takes requests until 23:00 ", Now).IsFailure);
        Assert.True(board.IsOpen);
        Assert.Equal("DJ Kace takes requests until 23:00", board.Note);
        Assert.IsType<RequestBoardOpened>(board.Events.Last());

        var invalid = board.Configure(open: true, 0, new string('x', RequestBoard.NoteMaxLength + 1), Now);
        Assert.Equal(2, invalid.Error!.SubErrors!.Count);

        var eventsBefore = board.Events.Count;
        Assert.False(board.Configure(open: true, 5, "DJ Kace takes requests until 23:00", Now).IsFailure);
        Assert.Equal(eventsBefore, board.Events.Count);

        Assert.False(board.Configure(open: false, 5, null, Now).IsFailure);
        Assert.IsType<RequestBoardClosed>(board.Events.Last());
        Assert.Equal(Now, board.ClosedAt);
    }

    [Fact]
    public void VoteNamesAreClipped()
    {
        var request = SongRequest.Create(
            new SongRequestId(Guid.NewGuid()),
            Guid.NewGuid(),
            Track.Typed("Suzanna", "Sauti Sol").Value!,
            new SongRequestVoteId(Guid.NewGuid()),
            Guid.NewGuid(),
            new string('n', SongRequestVote.NameMaxLength + 20),
            Now);

        Assert.Equal(SongRequestVote.NameMaxLength, request.Votes[0].Name.Length);
    }

    private static SongRequest NewRequest(Guid userId) =>
        SongRequest.Create(
            new SongRequestId(Guid.NewGuid()),
            Guid.NewGuid(),
            Track.Typed("Suzanna", "Sauti Sol").Value!,
            new SongRequestVoteId(Guid.NewGuid()),
            userId,
            "Alice Wanjiru",
            Now);
}
