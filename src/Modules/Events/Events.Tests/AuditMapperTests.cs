using Events.Domain.Common;
using Events.Domain.DomainEvents;
using Events.Domain.Model;
using Events.Infrastructure.Auditing;
using Xunit;

namespace Events.Tests;

public sealed class AuditMapperTests
{
    [Fact]
    public void StructureChangesMapToStableActionNames()
    {
        var @event = Event.Create(new EventId(Guid.CreateVersion7()), "krafthaus-001", "KraftHaus 001", null, null, null)
            .Value!;
        var itemId = Guid.CreateVersion7();

        var (action, target, metadata) = EventsAuditMapper.Map(
            new EventStructureChanged(@event.Id, EventPart.SalePhase, ChangeKind.Withdrawn, itemId),
            @event);

        Assert.Equal("events.event.sale_phase_withdrawn", action);
        Assert.Equal("event", target.Type);
        Assert.Equal(@event.Id.Value.ToString(), target.Id);
        Assert.Equal(itemId.ToString(), metadata["itemId"]);
    }

    [Fact]
    public void LifecycleEventsMapToActions()
    {
        var @event = Event.Create(new EventId(Guid.CreateVersion7()), "krafthaus-001", "KraftHaus 001", null, null, null)
            .Value!;

        Assert.Equal("events.event.published", EventsAuditMapper.Map(new EventPublished(@event.Id, DateTimeOffset.UtcNow), @event).Action);
        Assert.Equal("events.event.cancelled", EventsAuditMapper.Map(new EventCancelled(@event.Id, "x"), @event).Action);
        Assert.Equal("krafthaus-001", EventsAuditMapper.Map(@event.Events.First(), @event).Metadata["slug"]);
    }
}
