using System.Net;
using Common.Application.Analytics;
using Common.Application.EventCatalog;
using Common.Application.Tenancy;
using Common.Domain.ResultMonad;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Analytics.Application;

/// <summary><c>Analytics</c>: how long dashboards and the leaderboard are cached. Validated at start-up.</summary>
public sealed class AnalyticsOptions
{
    public const string SectionName = "Analytics";

    /// <summary>The admin dashboard is recomputed at most this often per event (0 turns caching off).</summary>
    public int DashboardCacheSeconds { get; init; } = 30;

    /// <summary>The public leaderboard is recomputed at most this often per event.</summary>
    public int LeaderboardCacheSeconds { get; init; } = 60;

    /// <summary>Krafts and songs the public leaderboard shows.</summary>
    public int LeaderboardSize { get; init; } = 10;
}

public sealed class AnalyticsOptionsValidator : IValidateOptions<AnalyticsOptions>
{
    public ValidateOptionsResult Validate(string? name, AnalyticsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options.DashboardCacheSeconds is < 0 or > 3600 ||
               options.LeaderboardCacheSeconds is < 0 or > 3600 ||
               options.LeaderboardSize is < 1 or > 50
            ? ValidateOptionsResult.Fail("Analytics: DashboardCacheSeconds and LeaderboardCacheSeconds 0-3600, LeaderboardSize 1-50.")
            : ValidateOptionsResult.Success;
    }
}

public static class AnalyticsErrors
{
    public static Error EventNotFound => new()
    {
        Key = "Analytics.EventNotFound",
        Description = "The event was not found.",
        StatusCode = HttpStatusCode.NotFound
    };
}

/// <summary>
///     The event dashboard (UF-023): sales, door, Krafts, songs and ratings in one view. Built on demand from each
///     module's insights interface (no copies of other modules' data) and cached briefly per tenant and event. A
///     module that is not running in this process simply leaves its section empty.
/// </summary>
public sealed class EventDashboardService(
    IEventCatalog events,
    ICurrentTenant tenant,
    IMemoryCache cache,
    IOptions<AnalyticsOptions> options,
    TimeProvider time,
    IEnumerable<ITicketingInsights> ticketing,
    IEnumerable<ICommerceInsights> commerce,
    IEnumerable<IMusicInsights> music,
    IEnumerable<IFeedbackInsights> feedback)
{
    public const int TopSongs = 10;

    public async Task<Result<EventDashboardView>> GetAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var key = $"analytics:dashboard:{(tenant.IsResolved ? tenant.TenantId.Value : Guid.Empty):N}:{eventId:N}";
        if (cache.TryGetValue(key, out EventDashboardView? cached) && cached is not null)
        {
            return cached;
        }

        var sale = eventId == Guid.Empty ? null : await events.GetSaleSnapshotAsync(eventId, cancellationToken);
        if (sale is null)
        {
            return AnalyticsErrors.EventNotFound;
        }

        var tickets = ticketing.FirstOrDefault() is { } t ? await t.GetAsync(eventId, cancellationToken) : null;
        var krafts = commerce.FirstOrDefault() is { } c ? await c.GetAsync(eventId, cancellationToken) : null;
        var songs = music.FirstOrDefault() is { } m ? await m.GetAsync(eventId, TopSongs, cancellationToken) : null;
        var ratings = feedback.FirstOrDefault() is { } f ? await f.GetAsync(eventId, cancellationToken) : null;

        var capacity = sale.Dates.Where(d => !d.Cancelled).SelectMany(d => d.Tiers).Where(x => !x.Withdrawn).Sum(x => x.Capacity);
        var sold = tickets is null ? 0 : tickets.Valid + tickets.CheckedIn + tickets.AwaitingBalance;

        var view = new EventDashboardView(
            eventId,
            sale.Title,
            time.GetUtcNow(),
            new HeadlineView(
                capacity,
                sold,
                Percent(sold, capacity),
                tickets?.CheckedIn ?? 0,
                Percent(tickets?.CheckedIn ?? 0, sold),
                tickets?.Waitlisted ?? 0,
                (tickets?.Collected ?? 0m) + (krafts?.Krafts.Sum(x => x.Gross) ?? 0m),
                (tickets?.PlatformFees ?? 0m) + (krafts?.Krafts.Sum(x => x.PlatformFees) ?? 0m),
                ratings?.Average),
            tickets,
            krafts,
            songs,
            ratings);

        var seconds = options.Value.DashboardCacheSeconds;
        if (seconds > 0)
        {
            cache.Set(key, view, TimeSpan.FromSeconds(seconds));
        }

        return view;
    }

    internal static decimal Percent(int part, int whole) => whole <= 0 ? 0m : Math.Round(100m * part / whole, 1);
}

/// <summary>The public leaderboard for an event: Krafts by orders and the most-requested songs. No figures, no names of people.</summary>
public sealed class LeaderboardService(
    IEventCatalog events,
    ICurrentTenant tenant,
    IMemoryCache cache,
    IOptions<AnalyticsOptions> options,
    IEnumerable<ICommerceInsights> commerce,
    IEnumerable<IMusicInsights> music)
{
    public async Task<Result<LeaderboardView>> GetAsync(string slug, CancellationToken cancellationToken)
    {
        var snapshot = string.IsNullOrWhiteSpace(slug) ? null : await events.FindBySlugAsync(slug, cancellationToken);
        if (snapshot is not { Status: EventLifecycle.Published or EventLifecycle.Completed })
        {
            return AnalyticsErrors.EventNotFound;
        }

        var key = $"analytics:leaderboard:{(tenant.IsResolved ? tenant.TenantId.Value : Guid.Empty):N}:{snapshot.EventId:N}";
        if (cache.TryGetValue(key, out LeaderboardView? cached) && cached is not null)
        {
            return cached;
        }

        var size = options.Value.LeaderboardSize;
        var krafts = commerce.FirstOrDefault() is { } c ? await c.GetAsync(snapshot.EventId, cancellationToken) : null;
        var songs = music.FirstOrDefault() is { } m ? await m.GetAsync(snapshot.EventId, size, cancellationToken) : null;

        var view = new LeaderboardView(
            snapshot.Slug,
            [.. (krafts?.Krafts ?? []).Where(x => x.PaidOrders > 0).Take(size).Select((x, i) => new LeaderboardKraftView(i + 1, x.KraftName, x.KraftSlug))],
            [.. (songs?.Top ?? []).Where(x => x.Votes > 0).Select((x, i) => new LeaderboardSongView(i + 1, x.Title, x.Artist, x.Votes))]);

        var seconds = options.Value.LeaderboardCacheSeconds;
        if (seconds > 0)
        {
            cache.Set(key, view, TimeSpan.FromSeconds(seconds));
        }

        return view;
    }
}

/// <param name="SellThroughPercent">Tickets sold ÷ capacity of open tiers on scheduled dates.</param>
/// <param name="CheckInPercent">Checked in ÷ sold.</param>
/// <param name="Revenue">Money collected on tickets and Kraft orders (gross, fees included).</param>
/// <param name="PlatformFees">The platform's share: ticket fees plus Kraft commission.</param>
public sealed record HeadlineView(
    int Capacity,
    int TicketsSold,
    decimal SellThroughPercent,
    int CheckedIn,
    decimal CheckInPercent,
    int Waitlisted,
    decimal Revenue,
    decimal PlatformFees,
    decimal? AverageRating);

public sealed record EventDashboardView(
    Guid EventId,
    string Title,
    DateTimeOffset GeneratedAt,
    HeadlineView Headline,
    TicketingInsights? Tickets,
    CommerceInsights? Krafts,
    MusicInsights? Songs,
    FeedbackInsights? Ratings);

public sealed record LeaderboardKraftView(int Rank, string Name, string Slug);

public sealed record LeaderboardSongView(int Rank, string Title, string Artist, int Votes);

public sealed record LeaderboardView(string EventSlug, IReadOnlyList<LeaderboardKraftView> Krafts, IReadOnlyList<LeaderboardSongView> Songs);
