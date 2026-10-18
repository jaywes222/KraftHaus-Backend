using Common.Application.Analytics;
using Music.Application.Abstractions;
using Music.Domain.Requests;

namespace Music.Application.Requests;

/// <summary>Song-request figures for the Analytics dashboard and leaderboard (UF-023). Songs and counts, never names.</summary>
public sealed class MusicInsightsProvider(ISongRequestRepository requests) : IMusicInsights
{
    public async Task<MusicInsights> GetAsync(Guid eventId, int top, CancellationToken cancellationToken)
    {
        var list = await requests.ListForEventAsync(eventId, cancellationToken);

        return new MusicInsights(
            list.Count,
            list.Sum(x => x.VoteCount),
            list.Count(x => x.Status == SongRequestStatus.Played),
            [.. list
                .OrderByDescending(x => x.VoteCount)
                .ThenBy(x => x.RequestedAt)
                .Take(Math.Clamp(top, 1, 50))
                .Select(x => new SongInsight(x.Track.Title, x.Track.Artist, x.VoteCount, x.Status.ToString()))]);
    }
}
