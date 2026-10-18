using Ticketing.Domain.Common;
using Ticketing.Domain.Waitlist;
using Ticketing.Infrastructure.Auditing;
using Xunit;

namespace Ticketing.Tests;

public sealed class AuditMapperTests
{
    [Fact]
    public void WaitlistEventsMapToStableActionsWithoutPersonalData()
    {
        var eventId = Guid.CreateVersion7();
        var entry = WaitlistEntry.Join(
                new WaitlistEntryId(Guid.CreateVersion7()),
                eventId,
                WaitlistEntryTests.Valid() with { Email = "wanjiru@example.com", MarketingConsent = true },
                18,
                DateTimeOffset.UtcNow)
            .Value!;

        var (action, target, metadata) = TicketingAuditMapper.Map(entry.Events.Single(), entry);

        Assert.Equal("ticketing.waitlist.joined", action);
        Assert.Equal("waitlist_entry", target.Type);
        Assert.Equal(entry.Id.Value.ToString(), target.Id);
        Assert.Equal(eventId.ToString(), metadata["eventId"]);
        Assert.Equal("on", metadata["marketingConsent"]);

        var values = string.Join('|', metadata.Values);
        Assert.DoesNotContain("712345678", values, StringComparison.Ordinal);
        Assert.DoesNotContain("Wanjiru", values, StringComparison.Ordinal);
        Assert.DoesNotContain("example.com", values, StringComparison.Ordinal);

        entry.MarkConfirmationSent(DateTimeOffset.UtcNow);
        Assert.Equal(
            "ticketing.waitlist.confirmation_sent",
            TicketingAuditMapper.Map(entry.Events.Last(), entry).Action);
    }
}
