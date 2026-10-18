using System.Net;
using System.Text.Json;
using Api.IntegrationTests.Identity.Auditing;
using Identity.Domain.Roles;
using Payments.Domain.Payments;
using Xunit;

namespace Api.IntegrationTests.Ticketing;

/// <summary>
///     The customer-paid platform fee (KES 50 a ticket, waived for the event's waitlist) and the admin's view of who
///     bought what: the event's tickets with search, and the money totals.
/// </summary>
[Collection(IdentityApiCollection.Name)]
public sealed class PlatformFeeAndSalesHttpTests(IdentityAuditApiFixture fixture)
{
    private readonly TicketingApi _api = new(fixture);

    [Fact]
    public async Task WaitlistedPhonesPayNoPlatformFee()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.AdminAsync(client);
        var sale = await TicketingApi.PublishedEventAsync(client, admin, price: 1000m);
        var waitlisted = await _api.CustomerAsync(client);
        var walkIn = await _api.CustomerAsync(client);

        using (var joined = await IdentityApi.CallAsync(client, HttpMethod.Post, "/api/client/waitlist", new
               {
                   eventSlug = sale.Slug,
                   name = "Wanjiru Kamau",
                   phone = "0" + waitlisted.Phone[4..],
                   age = 27,
                   gender = "non_binary",
                   location = "Kilimani",
                   email = (string?)null,
                   privacyAccepted = true,
                   privacyPolicyVersion = "2026-10-01",
                   marketingConsent = false
               }))
        {
            Assert.Equal(HttpStatusCode.Created, joined.StatusCode);
        }

        // The quote: the standard fee for anyone, nothing for the waitlisted phone.
        using (var anonymous = await IdentityApi.CallAsync(client, HttpMethod.Get, $"/api/client/events/{sale.Slug}/fees"))
        {
            var quote = await TicketingApi.ReadAsync(anonymous, HttpStatusCode.OK);
            Assert.Equal(50m, quote.GetProperty("ticketFeePerTicket").GetDecimal());
            Assert.Equal(30m, quote.GetProperty("foodOrderFee").GetDecimal());
            Assert.False(quote.GetProperty("waived").GetBoolean());
        }

        var mine = await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/client/events/{sale.Slug}/fees", waitlisted, null, HttpStatusCode.OK);
        Assert.True(mine.GetProperty("waived").GetBoolean());
        Assert.Equal(0m, mine.GetProperty("ticketFeePerTicket").GetDecimal());
        Assert.Equal(50m, mine.GetProperty("standardTicketFee").GetDecimal());

        var free = await PlaceAsync(client, waitlisted, sale, 2);
        Assert.Equal(2000m, free.GetProperty("total").GetDecimal());
        Assert.Equal(0m, free.GetProperty("platformFee").GetDecimal());
        Assert.True(free.GetProperty("feeWaived").GetBoolean());
        Assert.Equal(2000m, Assert.Single(fixture.PaymentProvider.PromptsTo(waitlisted.Phone)).Request.Amount);

        var charged = await PlaceAsync(client, walkIn, sale, 2);
        Assert.Equal(2100m, charged.GetProperty("total").GetDecimal());
        Assert.Equal(100m, charged.GetProperty("platformFee").GetDecimal());
        Assert.False(charged.GetProperty("feeWaived").GetBoolean());
    }

    [Fact]
    public async Task AdminsSeeTheEventsTicketsAndTotals()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.AdminAsync(client);
        var support = await _api.AdminAsync(client, IdentityRole.Support);
        var dj = await _api.AdminAsync(client, IdentityRole.Dj);
        var sale = await TicketingApi.PublishedEventAsync(client, admin, price: 1500m);
        var buyer = await _api.CustomerAsync(client);
        var bought = await _api.BuyAsync(client, buyer, sale, 3);

        var path = $"/api/admin/events/{sale.EventId}/tickets";
        var all = await TicketingApi.SendAsync(client, HttpMethod.Get, path, support, null, HttpStatusCode.OK);
        Assert.Equal(3, all.GetArrayLength());
        Assert.All(all.EnumerateArray(), x =>
        {
            Assert.Equal("Valid", x.GetProperty("status").GetString());
            Assert.Equal(buyer.Phone[^3..], x.GetProperty("holderPhoneLast3").GetString());
        });

        // Search by code prefix, by the last phone digits, and filter by status.
        var code = bought[0].GetProperty("code").GetString()!;
        var byCode = await TicketingApi.SendAsync(client, HttpMethod.Get, $"{path}?q={code[..6]}", support, null, HttpStatusCode.OK);
        Assert.Contains(byCode.EnumerateArray(), x => x.GetProperty("code").GetString() == code);
        var byPhone = await TicketingApi.SendAsync(client, HttpMethod.Get, $"{path}?q={buyer.Phone[^4..]}", support, null, HttpStatusCode.OK);
        Assert.Equal(3, byPhone.GetArrayLength());
        var checkedIn = await TicketingApi.SendAsync(client, HttpMethod.Get, $"{path}?status=CheckedIn", support, null, HttpStatusCode.OK);
        Assert.Equal(0, checkedIn.GetArrayLength());

        var sales = await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/admin/events/{sale.EventId}/sales", admin, null, HttpStatusCode.OK);
        Assert.Equal(3, sales.GetProperty("tickets").GetProperty("valid").GetInt32());
        Assert.Equal(1, sales.GetProperty("paidOrders").GetInt32());
        Assert.Equal(4650m, sales.GetProperty("amountCollected").GetDecimal());
        Assert.Equal(150m, sales.GetProperty("platformFees").GetDecimal());

        // Not for a role without tickets:view.
        await TicketingApi.SendAsync(client, HttpMethod.Get, path, dj, null, HttpStatusCode.Forbidden);
    }

    private async Task<JsonElement> PlaceAsync(HttpClient client, IdentityLogin customer, TicketingApi.SaleEvent sale, int quantity)
    {
        using var placed = await TicketingApi.PlaceOrderAsync(
            client,
            customer,
            new { eventSlug = sale.Slug, items = new[] { new { tierId = sale.TierId, quantity } } },
            Guid.NewGuid().ToString("N"));
        var order = await TicketingApi.ReadAsync(placed, HttpStatusCode.Created);

        var prompt = fixture.PaymentProvider.PromptsTo(customer.Phone)[^1];
        await _api.CompletePromptAsync(client, prompt, ProviderTransactionStatus.Success);
        await TicketingApi.WaitForOrderAsync(client, customer, order.GetProperty("id").GetGuid(), "Paid");

        return order;
    }
}
