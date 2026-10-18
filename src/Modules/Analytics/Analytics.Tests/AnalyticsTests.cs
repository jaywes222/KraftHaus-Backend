using Analytics.Application;
using Common.Application.Analytics;
using Common.Application.EventCatalog;
using Common.Application.Tenancy;
using Common.Domain.Tenancy;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Xunit;

namespace Analytics.Tests;

public sealed class AnalyticsTests
{
    private static readonly Guid EventId = Guid.CreateVersion7();

    [Fact]
    public async Task TheDashboardCombinesModulesAndCachesBriefly()
    {
        var ticketing = new FakeTicketing();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = Dashboard(cache, ticketing, krafts: [new KraftInsight(Guid.CreateVersion7(), "Nyama Zone", "nyama-zone", 10, 25, 5000m, 500m)]);

        var view = (await service.GetAsync(EventId, CancellationToken.None)).Value!;

        Assert.Equal(200, view.Headline.Capacity);
        Assert.Equal(50, view.Headline.TicketsSold);
        Assert.Equal(25m, view.Headline.SellThroughPercent);
        Assert.Equal(40m, view.Headline.CheckInPercent);
        Assert.Equal(55_000m, view.Headline.Revenue);
        Assert.Equal(2_500m, view.Headline.PlatformFees);
        Assert.Equal(4.2m, view.Headline.AverageRating);

        await service.GetAsync(EventId, CancellationToken.None);
        Assert.Equal(1, ticketing.Calls);
    }

    [Fact]
    public async Task MissingModulesLeaveTheirSectionEmptyAndUnknownEventsAreNotFound()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = new EventDashboardService(new FakeEvents(), new FakeTenant(), cache, Options.Create(new AnalyticsOptions()), TimeProvider.System, [], [], [], []);

        var view = (await service.GetAsync(EventId, CancellationToken.None)).Value!;
        Assert.Null(view.Tickets);
        Assert.Equal(0, view.Headline.TicketsSold);
        Assert.Equal(0m, view.Headline.CheckInPercent);

        Assert.Equal("Analytics.EventNotFound", (await service.GetAsync(Guid.CreateVersion7(), CancellationToken.None)).Error!.Key);
    }

    [Fact]
    public async Task TheLeaderboardRanksWithoutFigures()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var krafts = new FakeCommerce([
            new KraftInsight(Guid.CreateVersion7(), "Busy", "busy", 30, 60, 9000m, 900m),
            new KraftInsight(Guid.CreateVersion7(), "Quiet", "quiet", 2, 2, 400m, 40m),
            new KraftInsight(Guid.CreateVersion7(), "Nothing yet", "nothing", 0, 0, 0m, 0m)
        ]);
        var service = new LeaderboardService(new FakeEvents(), new FakeTenant(), cache, Options.Create(new AnalyticsOptions()), [krafts], [new FakeMusic()]);

        var board = (await service.GetAsync("krafthaus-001", CancellationToken.None)).Value!;

        Assert.Equal(["Busy", "Quiet"], board.Krafts.Select(x => x.Name));
        Assert.Equal(1, board.Krafts[0].Rank);
        Assert.Equal("Suzanna", Assert.Single(board.Songs).Title);
        Assert.True((await service.GetAsync("draft-event", CancellationToken.None)).IsFailure);
    }

    private static EventDashboardService Dashboard(IMemoryCache cache, FakeTicketing ticketing, IReadOnlyList<KraftInsight> krafts) =>
        new(new FakeEvents(), new FakeTenant(), cache, Options.Create(new AnalyticsOptions()), TimeProvider.System,
            [ticketing], [new FakeCommerce(krafts)], [new FakeMusic()], [new FakeFeedback()]);

    private sealed class FakeEvents : IEventCatalog
    {
        public Task<EventSnapshot?> FindBySlugAsync(string slug, CancellationToken cancellationToken) =>
            Task.FromResult<EventSnapshot?>(new EventSnapshot(EventId, slug, "KRAFTHAUS 001",
                slug == "draft-event" ? EventLifecycle.Draft : EventLifecycle.Published, null, null));

        public Task<EventSaleSnapshot?> GetSaleSnapshotAsync(Guid eventId, CancellationToken cancellationToken)
        {
            if (eventId != EventId)
            {
                return Task.FromResult<EventSaleSnapshot?>(null);
            }

            var now = DateTimeOffset.UtcNow;
            var tiers = new[]
            {
                new SaleTier(Guid.CreateVersion7(), "Regular", 150, false, [], []),
                new SaleTier(Guid.CreateVersion7(), "VIP", 50, false, [], []),
                new SaleTier(Guid.CreateVersion7(), "Withdrawn", 999, true, [], [])
            };

            return Task.FromResult<EventSaleSnapshot?>(new EventSaleSnapshot(EventId, "krafthaus-001", "KRAFTHAUS 001", null, EventLifecycle.Published,
                new EventSalePolicies(false, true, 1, false, 10, 10),
                [new SaleDate(Guid.CreateVersion7(), null, now, now.AddHours(8), null, false, tiers),
                 new SaleDate(Guid.CreateVersion7(), null, now, now.AddHours(8), null, true, tiers)]));
        }
    }

    private sealed class FakeTenant : ICurrentTenant
    {
        public bool IsResolved => true;

        public TenantId TenantId { get; } = new(Guid.CreateVersion7());

        public IdentityScope IdentityScope => IdentityScope.Platform;
    }

    private sealed class FakeTicketing : ITicketingInsights
    {
        public int Calls { get; private set; }

        public Task<TicketingInsights> GetAsync(Guid eventId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new TicketingInsights(120, 80, [], [], [], 25, 20, 5, 1, 40, 50_000m, 2_000m, []));
        }
    }

    private sealed class FakeCommerce(IReadOnlyList<KraftInsight> krafts) : ICommerceInsights
    {
        public Task<CommerceInsights> GetAsync(Guid eventId, CancellationToken cancellationToken) =>
            Task.FromResult(new CommerceInsights(krafts, [], []));
    }

    private sealed class FakeMusic : IMusicInsights
    {
        public Task<MusicInsights> GetAsync(Guid eventId, int top, CancellationToken cancellationToken) =>
            Task.FromResult(new MusicInsights(2, 3, 0, [new SongInsight("Suzanna", "Sauti Sol", 3, "Waiting"), new SongInsight("Silent", "Nobody", 0, "Passed")]));
    }

    private sealed class FakeFeedback : IFeedbackInsights
    {
        public Task<FeedbackInsights> GetAsync(Guid eventId, CancellationToken cancellationToken) =>
            Task.FromResult(new FeedbackInsights(10, 4.2m, [0, 0, 1, 6, 3], new Dictionary<string, decimal>()));
    }
}
