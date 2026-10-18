using System.Net;
using Api.IntegrationTests.Identity.Auditing;
using Api.IntegrationTests.Ticketing;
using Identity.Domain.Roles;
using Payments.Domain.Payments;
using Xunit;

namespace Api.IntegrationTests.Commerce;

/// <summary>
///     The KRAFTHAUS till: set in admin, tested and approved like a Kraft till, then ticket money goes to its channel.
///     A Kraft Owner sets their own Kraft's till but never the platform's.
/// </summary>
[Collection(IdentityApiCollection.Name)]
public sealed class PlatformTillHttpTests(IdentityAuditApiFixture fixture)
{
    private readonly CommerceApi _api = new(fixture);

    [Fact]
    public async Task TheApprovedPlatformTillTakesTicketMoneyUntilDisabled()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.Ticketing.AdminAsync(client);
        var finance = await _api.Ticketing.AdminAsync(client, IdentityRole.Finance);
        var sale = await TicketingApi.PublishedEventAsync(client, admin);

        // A Kraft Owner sets their own till, not the platform's.
        var kraftId = await CommerceApi.CreateKraftAsync(client, admin, "Owner Grill", sale.EventId);
        var owner = await _api.KraftStaffAsync(client, kraftId);
        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/krafts/{kraftId}/tills", owner,
            new { eventId = sale.EventId, name = "Grill paybill", shortcodeType = "Paybill", shortcode = "400200", accountNumber = "GRILL" },
            HttpStatusCode.Created);
        await TicketingApi.SendAsync(client, HttpMethod.Post, "/api/admin/platform-till", owner,
            new { name = "Not mine", shortcodeType = "BuyGoods", shortcode = "123456", accountNumber = (string?)null }, HttpStatusCode.Forbidden);

        var till = await TicketingApi.SendAsync(client, HttpMethod.Post, "/api/admin/platform-till", finance,
            new { name = "KRAFTHAUS paybill", shortcodeType = "Paybill", shortcode = "880100", accountNumber = "KH001" }, HttpStatusCode.Created);
        var tillId = till.GetProperty("id").GetGuid();
        Assert.Equal("PlatformTill", till.GetProperty("kind").GetString());
        Assert.Equal("Submitted", till.GetProperty("status").GetString());

        try
        {
            await TicketingApi.SendAsync(client, HttpMethod.Post, "/api/admin/platform-till", finance,
                new { name = "Second", shortcodeType = "BuyGoods", shortcode = "654321", accountNumber = (string?)null }, HttpStatusCode.Conflict);

            // Not approved yet: tickets still use the configured channel.
            var buyer = await _api.Ticketing.CustomerAsync(client);
            await _api.Ticketing.BuyAsync(client, buyer, sale, 1);
            Assert.Null(fixture.PaymentProvider.PromptsTo(buyer.Phone)[^1].Request.ChannelId);

            var channelId = Guid.NewGuid();
            await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/tills/{tillId}/test", finance,
                new { channelId, phone = finance.Phone }, HttpStatusCode.OK);
            var prompt = fixture.PaymentProvider.PromptsTo(finance.Phone)[^1];
            Assert.Equal(channelId, prompt.Request.ChannelId);
            await _api.Ticketing.CompletePromptAsync(client, prompt, ProviderTransactionStatus.Success);
            await CommerceApi.WaitForTillAsync(client, finance, tillId, "Passed");
            await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/tills/{tillId}/approve", finance, null, HttpStatusCode.OK);

            var list = await TicketingApi.SendAsync(client, HttpMethod.Get, "/api/admin/platform-till", finance, null, HttpStatusCode.OK);
            Assert.Equal("Approved", list.EnumerateArray().First().GetProperty("status").GetString());

            var second = await _api.Ticketing.CustomerAsync(client);
            await _api.Ticketing.BuyAsync(client, second, sale, 1);
            Assert.Equal(channelId, fixture.PaymentProvider.PromptsTo(second.Phone)[^1].Request.ChannelId);
        }
        finally
        {
            // Leave the tenant as other tests expect it: no platform till.
            var current = await TicketingApi.SendAsync(client, HttpMethod.Get, "/api/admin/platform-till", finance, null, HttpStatusCode.OK);
            var state = current.EnumerateArray().First().GetProperty("status").GetString();
            var action = state == "Approved" ? "disable" : "reject";
            await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/tills/{tillId}/{action}", finance,
                new { note = "Test over" }, HttpStatusCode.OK);
        }

        var third = await _api.Ticketing.CustomerAsync(client);
        await _api.Ticketing.BuyAsync(client, third, sale, 1);
        Assert.Null(fixture.PaymentProvider.PromptsTo(third.Phone)[^1].Request.ChannelId);
    }
}
