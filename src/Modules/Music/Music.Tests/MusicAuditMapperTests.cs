using Music.Domain.Common;
using Music.Domain.Requests;
using Music.Infrastructure.Auditing;
using Xunit;

namespace Music.Tests;

public sealed class MusicAuditMapperTests
{
    [Fact]
    public void EveryMusicEventHasAnActionAndNoPersonalData()
    {
        var now = DateTimeOffset.UtcNow;
        var board = RequestBoard.Create(new RequestBoardId(Guid.NewGuid()), Guid.NewGuid(), 3);
        board.Configure(open: true, 4, "Note", now);
        board.Configure(open: true, 5, "Note", now);
        board.Configure(open: false, 5, "Note", now);

        var request = SongRequest.Create(
            new SongRequestId(Guid.NewGuid()),
            board.EventId,
            Track.Typed("Suzanna", "Sauti Sol").Value!,
            new SongRequestVoteId(Guid.NewGuid()),
            Guid.NewGuid(),
            "Alice Wanjiru",
            now);
        request.Second(new SongRequestVoteId(Guid.NewGuid()), Guid.NewGuid(), "Bob Otieno", now);
        request.MoveTo(SongRequestStatus.Played, Guid.NewGuid(), now);

        var actions = board.Events.Select(e => MusicAuditMapper.Map(e, board))
            .Concat(request.Events.Select(e => MusicAuditMapper.Map(e, request)))
            .ToList();

        Assert.Equal(
            ["music.board.opened", "music.board.updated", "music.board.closed", "music.request.created", "music.request.seconded", "music.request.status_changed"],
            actions.Select(x => x.Action));

        var values = actions.SelectMany(x => x.Metadata.Values).ToList();
        Assert.DoesNotContain(values, v => v.Contains("Alice", StringComparison.Ordinal) || v.Contains("Bob", StringComparison.Ordinal));
        Assert.DoesNotContain(values, v => v.Contains("Suzanna", StringComparison.Ordinal));
        Assert.Equal("Played", actions[^1].Metadata["to"]);
    }
}
