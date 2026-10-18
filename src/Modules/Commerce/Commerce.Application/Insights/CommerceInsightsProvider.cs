using Commerce.Application.Abstractions;
using Common.Application.Analytics;

namespace Commerce.Application.Insights;

/// <summary>Kraft sales for the Analytics dashboard and leaderboard (UF-023): paid orders only, no customers.</summary>
public sealed class CommerceInsightsProvider(IKraftOrderRepository orders, IKraftRepository krafts) : ICommerceInsights
{
    public const int TopItems = 10;

    public async Task<CommerceInsights> GetAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var paid = await orders.ListPaidAsync(eventId, null, cancellationToken);
        var kraftIds = paid.Select(x => x.KraftId).Distinct().ToList();
        var slugs = (await krafts.ListByIdsForReadAsync(kraftIds, cancellationToken)).ToDictionary(x => x.Id.Value, x => x.Slug);

        var kraftRows = paid
            .GroupBy(x => x.KraftId)
            .Select(x => new KraftInsight(
                x.Key,
                x.First().KraftName,
                slugs.GetValueOrDefault(x.Key, string.Empty),
                x.Count(),
                x.Sum(o => o.Lines.Where(l => !l.Unavailable).Sum(l => l.Quantity)),
                x.Sum(o => o.Total),
                x.Sum(o => o.PlatformFee ?? 0m)))
            .OrderByDescending(x => x.PaidOrders)
            .ThenByDescending(x => x.Gross)
            .ToList();

        var items = paid
            .SelectMany(o => o.Lines.Where(l => !l.Unavailable).Select(l => (o.KraftId, o.KraftName, Line: l)))
            .GroupBy(x => (x.KraftId, x.Line.MenuItemId))
            .Select(x => new MenuItemInsight(
                x.Key.KraftId,
                x.First().KraftName,
                x.First().Line.Name,
                x.Sum(l => l.Line.Quantity),
                x.Sum(l => l.Line.UnitPrice * l.Line.Quantity)))
            .OrderByDescending(x => x.Portions)
            .Take(TopItems)
            .ToList();

        var byHour = paid
            .Where(x => x.PaidAt is not null)
            .GroupBy(x => Hour(x.PaidAt!.Value))
            .OrderBy(x => x.Key)
            .Select(x => new SlotCount(null, x.Key, x.Count()))
            .ToList();

        return new CommerceInsights(kraftRows, items, byHour);
    }

    private static DateTimeOffset Hour(DateTimeOffset instant)
    {
        var utc = instant.ToUniversalTime();
        return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero);
    }
}
