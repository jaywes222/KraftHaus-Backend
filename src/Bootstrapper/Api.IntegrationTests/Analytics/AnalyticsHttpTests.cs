using System.Net;
using Analytics.Domain;
using Api.IntegrationTests.Identity.Auditing;
using Api.IntegrationTests.Ticketing;
using Identity.Domain.Authorization;
using Identity.Domain.Roles;
using Xunit;

namespace Api.IntegrationTests.Analytics;

/// <summary>UF-023: the event dashboard adds up sales, the door, Krafts and songs; the public leaderboard shows no figures.</summary>
[Collection(IdentityApiCollection.Name)]
public sealed class AnalyticsHttpTests(IdentityAuditApiFixture fixture)
{
    private readonly TicketingApi _api = new(fixture);

    [Fact]
    public async Task TheDashboardReflectsSalesAndTheDoor()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.AdminAsync(client);
        var finance = await _api.AdminAsync(client, IdentityRole.Finance);
        var support = await _api.AdminAsync(client, IdentityRole.Support);
        var sale = await TicketingApi.PublishedEventAsync(client, admin, capacity: 40);

        var guest = await _api.CustomerAsync(client);
        var tickets = await _api.BuyAsync(client, guest, sale, 2);
        await TicketingApi.SendAsync(client, HttpMethod.Post, "/api/ops/check-in/manual", support,
            new { eventDateId = sale.DateId, code = tickets[0].GetProperty("code").GetString(), ticketId = (Guid?)null }, HttpStatusCode.OK);

        var path = $"/api/admin/events/{sale.EventId}/dashboard";
        await TicketingApi.SendAsync(client, HttpMethod.Get, path, support, null, HttpStatusCode.Forbidden);
        await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/admin/events/{Guid.NewGuid()}/dashboard", admin, null, HttpStatusCode.NotFound);

        var dashboard = await TicketingApi.SendAsync(client, HttpMethod.Get, path, finance, null, HttpStatusCode.OK);
        var headline = dashboard.GetProperty("headline");
        Assert.Equal(40, headline.GetProperty("capacity").GetInt32());
        Assert.Equal(2, headline.GetProperty("ticketsSold").GetInt32());
        Assert.Equal(5m, headline.GetProperty("sellThroughPercent").GetDecimal());
        Assert.Equal(1, headline.GetProperty("checkedIn").GetInt32());
        Assert.Equal(50m, headline.GetProperty("checkInPercent").GetDecimal());
        Assert.True(headline.GetProperty("revenue").GetDecimal() >= 2000m);

        var tiers = dashboard.GetProperty("tickets").GetProperty("tiers");
        Assert.Equal(2, Assert.Single(tiers.EnumerateArray()).GetProperty("sold").GetInt32());
        Assert.Single(dashboard.GetProperty("tickets").GetProperty("checkInsByQuarterHour").EnumerateArray());
        Assert.DoesNotContain(guest.Phone, dashboard.GetRawText(), StringComparison.Ordinal);

        // The leaderboard is public and has nothing to show yet (no Kraft orders, no songs).
        using var board = await IdentityApi.CallAsync(client, HttpMethod.Get, $"/api/client/events/{sale.Slug}/leaderboard");
        Assert.Equal(HttpStatusCode.OK, board.StatusCode);
    }

    [Fact]
    public void AnalyticsPermissionsAreInIdentitysCatalog() =>
        Assert.Subset(IdentityPermission.All.ToHashSet(StringComparer.Ordinal), AnalyticsPermission.All.ToHashSet(StringComparer.Ordinal));
}
