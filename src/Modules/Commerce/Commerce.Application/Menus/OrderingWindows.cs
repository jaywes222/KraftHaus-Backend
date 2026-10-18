using Common.Application.EventCatalog;

namespace Commerce.Application.Menus;

/// <summary>When an event takes reservations (before the day) and live orders (on the day), from its dates.</summary>
public sealed record OrderingWindows(
    DateTimeOffset? ReservationsCloseAt,
    DateTimeOffset? LiveOpensAt,
    DateTimeOffset? LiveClosesAt)
{
    public static OrderingWindows For(EventSnapshot snapshot, CommerceOptions options)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(options);

        if (snapshot.FirstStartsAt is not { } start || snapshot.LastEndsAt is not { } end)
        {
            return new OrderingWindows(null, null, null);
        }

        return new OrderingWindows(
            start.AddHours(-options.ReservationsCloseHoursBeforeStart),
            start.AddMinutes(-options.LiveOrderingOpensMinutesBeforeStart),
            end);
    }

    public bool ReservationsOpenAt(DateTimeOffset now, EventLifecycle status) =>
        status == EventLifecycle.Published && ReservationsCloseAt is { } close && now < close;

    public bool LiveOpenAt(DateTimeOffset now, EventLifecycle status) =>
        status == EventLifecycle.Published && LiveOpensAt is { } open && LiveClosesAt is { } close && now >= open && now < close;

    /// <summary>
    ///     The event date live orders are taken for at <paramref name="now" />: a scheduled date whose window (opening
    ///     <see cref="CommerceOptions.LiveOrderingOpensMinutesBeforeStart" /> before it starts, until it ends) contains now.
    ///     When windows overlap, the date that started last wins. <c>null</c> between dates.
    /// </summary>
    public static Guid? LiveDateAt(IEnumerable<SaleDate> dates, DateTimeOffset now, CommerceOptions options)
    {
        ArgumentNullException.ThrowIfNull(dates);
        ArgumentNullException.ThrowIfNull(options);

        return dates
            .Where(x => !x.Cancelled && now >= x.StartsAt.AddMinutes(-options.LiveOrderingOpensMinutesBeforeStart) && now < x.EndsAt)
            .OrderByDescending(x => x.StartsAt)
            .Select(x => (Guid?)x.DateId)
            .FirstOrDefault();
    }
}
