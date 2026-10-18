using Common.Application.EventCatalog;

namespace Ticketing.Application.Waitlist;

/// <summary>
///     When an event's waitlist accepts sign-ups. Derived from the event (no separate admin switch yet): open while
///     the event is published and its first scheduled date has not started. Cancelled, completed or past events are
///     closed. Drafts are not public, so they are treated as not found before this rule is asked.
/// </summary>
public static class WaitlistPolicy
{
    public static bool IsOpen(EventSnapshot @event, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(@event);

        return @event.Status == EventLifecycle.Published &&
               @event.FirstStartsAt is { } firstStart &&
               now < firstStart;
    }
}
