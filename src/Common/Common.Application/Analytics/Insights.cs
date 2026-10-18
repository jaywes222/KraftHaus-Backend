namespace Common.Application.Analytics;

/// <summary>
///     Read-only figures each module gives the Analytics dashboards (UF-023). No personal data crosses these
///     interfaces: counts and amounts only. Days are calendar days in Nairobi.
/// </summary>
public interface ITicketingInsights
{
    Task<TicketingInsights> GetAsync(Guid eventId, CancellationToken cancellationToken);
}

public interface ICommerceInsights
{
    Task<CommerceInsights> GetAsync(Guid eventId, CancellationToken cancellationToken);
}

public interface IMusicInsights
{
    Task<MusicInsights> GetAsync(Guid eventId, int top, CancellationToken cancellationToken);
}

public interface IFeedbackInsights
{
    Task<FeedbackInsights> GetAsync(Guid eventId, CancellationToken cancellationToken);
}

public sealed record DayCount(DateOnly Day, int Count);

public sealed record DayAmount(DateOnly Day, int Count, decimal Amount);

/// <param name="SlotStart">UTC start of a 15-minute (check-ins) or 60-minute (orders) slot.</param>
public sealed record SlotCount(Guid? EventDateId, DateTimeOffset SlotStart, int Count);

/// <param name="Sold">Tickets valid, checked in or awaiting a Lipa Mdogo Mdogo balance.</param>
public sealed record TierCount(Guid TierId, string Name, int Sold, int CheckedIn);

/// <param name="SalesByDay">Paid orders by the day the first money came in: orders and amount collected.</param>
public sealed record TicketingInsights(
    int Waitlisted,
    int WaitlistMarketingConsent,
    IReadOnlyList<DayCount> WaitlistByDay,
    IReadOnlyList<DayAmount> SalesByDay,
    IReadOnlyList<TierCount> Tiers,
    int Valid,
    int CheckedIn,
    int AwaitingBalance,
    int Void,
    int PaidOrders,
    decimal Collected,
    decimal PlatformFees,
    IReadOnlyList<SlotCount> CheckInsByQuarterHour);

public sealed record KraftInsight(Guid KraftId, string KraftName, string KraftSlug, int PaidOrders, int Portions, decimal Gross, decimal PlatformFees);

public sealed record MenuItemInsight(Guid KraftId, string KraftName, string ItemName, int Portions, decimal Gross);

public sealed record CommerceInsights(
    IReadOnlyList<KraftInsight> Krafts,
    IReadOnlyList<MenuItemInsight> TopItems,
    IReadOnlyList<SlotCount> OrdersByHour);

public sealed record SongInsight(string Title, string Artist, int Votes, string Status);

public sealed record MusicInsights(int Requests, int Votes, int Played, IReadOnlyList<SongInsight> Top);

/// <param name="Average">Mean of the 1-5 overall scores; <c>null</c> without ratings.</param>
/// <param name="Distribution">Ratings per score, index 0 = one star … index 4 = five stars.</param>
public sealed record FeedbackInsights(int Ratings, decimal? Average, IReadOnlyList<int> Distribution, IReadOnlyDictionary<string, decimal> AspectAverages);
