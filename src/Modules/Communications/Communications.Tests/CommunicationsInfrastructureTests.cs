using Common.Domain.Communications;
using Common.Domain.Events;
using Communications.Domain.Broadcasts;
using Communications.Domain.Common;
using Communications.Domain.OptOuts;
using Communications.Domain.Templates;
using Communications.Infrastructure.Auditing;
using Communications.Infrastructure.Endpoints;
using Xunit;

namespace Communications.Tests;

public sealed class CommunicationsInfrastructureTests
{
    [Fact]
    public void DeliveryReportsAreReadFromTheShapesProvidersPost()
    {
        var single = Assert.Single(DeliveryReportWebhook.Parse("""{"messageId":"m-1","status":"DeliveredToTerminal"}"""));
        Assert.Equal(("m-1", "DeliveredToTerminal"), (single.ProviderMessageId, single.Status));

        var list = DeliveryReportWebhook.Parse("""[{"message_id":"m-2","deliveryStatus":"Failed"},{"id":7,"state":"delivered"}]""");
        Assert.Equal(["m-2", "7"], list.Select(x => x.ProviderMessageId));

        var wrapped = Assert.Single(DeliveryReportWebhook.Parse("""{"data":[{"msgId":"m-3","dlrStatus":"DELIVRD"}]}"""));
        Assert.Equal("m-3", wrapped.ProviderMessageId);

        Assert.Empty(DeliveryReportWebhook.Parse("not json"));
        Assert.Empty(DeliveryReportWebhook.Parse("42"));
    }

    [Fact]
    public void TheWebhookTokenIsComparedExactly()
    {
        const string token = "a-very-long-delivery-report-token";

        Assert.True(DeliveryReportWebhook.TokenMatches(token, token));
        Assert.False(DeliveryReportWebhook.TokenMatches(token + "x", token));
        Assert.False(DeliveryReportWebhook.TokenMatches(null, token));
    }

    [Fact]
    public void EveryCommunicationsEventHasAnAuditActionWithoutPersonalData()
    {
        var broadcast = Broadcast.Create(new BroadcastId(Guid.CreateVersion7()), "Gates", Guid.CreateVersion7(), BroadcastAudience.TicketHolders, "Gates open").Value!;
        var events = new DomainEvent[]
        {
            new BroadcastCreated(broadcast.Id, broadcast.EventId, BroadcastAudience.TicketHolders),
            new BroadcastUpdated(broadcast.Id, broadcast.EventId, BroadcastAudience.TicketHolders),
            new BroadcastScheduled(broadcast.Id, broadcast.EventId, DateTimeOffset.UnixEpoch),
            new BroadcastCancelled(broadcast.Id, broadcast.EventId),
            new BroadcastStarted(broadcast.Id, broadcast.EventId, 3, 1),
            new BroadcastCompleted(broadcast.Id, broadcast.EventId, 2, 1),
            new PhoneOptedOut(new OptOutId(Guid.CreateVersion7()), OptOutSource.Customer),
            new PhoneOptedIn(new OptOutId(Guid.CreateVersion7()), OptOutSource.Staff),
            new MessageTemplateSaved(new MessageTemplateId(Guid.CreateVersion7())),
            new MessageTemplateArchived(new MessageTemplateId(Guid.CreateVersion7()))
        };

        var actions = events.Select(e => CommunicationsAuditMapper.Map(e, broadcast)).ToList();

        Assert.Equal(events.Length, actions.Select(x => x.Action).Distinct().Count());
        Assert.All(actions, x => Assert.StartsWith("communications.", x.Action, StringComparison.Ordinal));
        Assert.All(actions, x => Assert.DoesNotContain(x.Metadata.Values, v => v.StartsWith('+')));
    }
}
