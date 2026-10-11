using Common.Domain.ResultMonad;
using Events.Application.Abstractions;
using Events.Domain.Common;
using Events.Domain.Model;

namespace Events.Application.Public;

/// <summary>
///     What customers and visitors see on the client site: upcoming published events and one event by slug. Drafts
///     never appear; a cancelled or completed event is still reachable by its link so buyers can see its status.
/// </summary>
public sealed class PublicEventService(
    IEventRepository events,
    TimeProvider time)
{
    public async Task<Result<IReadOnlyList<PublicEventCard>>> ListUpcomingAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var list = await events.ListUpcomingPublishedAsync(now, cancellationToken);

        return Result<IReadOnlyList<PublicEventCard>>.Success(
        [
            .. list
                .Select(x => x.ToCard(now))
                .OrderBy(x => x.NextStartsAt)
        ]);
    }

    public async Task<Result<PublicEvent>> GetBySlugAsync(string slug, CancellationToken cancellationToken)
    {
        var valid = Event.ValidateSlug(slug);
        if (valid.IsFailure)
        {
            return NotFound();
        }

        var @event = await events.FindBySlugForReadAsync(valid.Value!, cancellationToken);

        return @event is null || @event.Status == EventStatus.Draft
            ? NotFound()
            : @event.ToPublic(time.GetUtcNow());
    }

    private static Error NotFound() =>
        new()
        {
            Key = "Events.EventNotFound",
            Description = "The event was not found.",
            StatusCode = System.Net.HttpStatusCode.NotFound
        };
}
