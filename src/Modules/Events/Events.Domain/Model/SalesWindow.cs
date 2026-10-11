using Common.Domain.ResultMonad;
using Events.Domain.Common;

namespace Events.Domain.Model;

/// <summary>A half-open UTC interval <c>[StartsAt, EndsAt)</c> during which something is on sale.</summary>
public readonly record struct SalesWindow
{
    private SalesWindow(DateTimeOffset startsAt, DateTimeOffset endsAt)
    {
        StartsAt = startsAt;
        EndsAt = endsAt;
    }

    public DateTimeOffset StartsAt { get; }

    public DateTimeOffset EndsAt { get; }

    public static Result<SalesWindow> Create(DateTimeOffset startsAt, DateTimeOffset endsAt)
    {
        var start = startsAt.ToUniversalTime();
        var end = endsAt.ToUniversalTime();

        return end <= start
            ? EventsErrors.Invalid("SalesWindow", "A sales window must end after it starts.")
            : new SalesWindow(start, end);
    }

    /// <summary>Rehydrates a stored window (already validated when it was created).</summary>
    internal static SalesWindow FromStored(DateTimeOffset startsAt, DateTimeOffset endsAt) => new(startsAt, endsAt);

    public bool Contains(DateTimeOffset instant) => instant >= StartsAt && instant < EndsAt;

    public bool Overlaps(SalesWindow other) => StartsAt < other.EndsAt && other.StartsAt < EndsAt;

    public bool HasStarted(DateTimeOffset now) => now >= StartsAt;

    public bool HasEnded(DateTimeOffset now) => now >= EndsAt;
}
