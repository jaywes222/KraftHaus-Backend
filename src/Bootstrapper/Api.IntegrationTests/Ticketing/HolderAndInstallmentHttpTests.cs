using System.Net;
using System.Text.Json;
using Api.IntegrationTests.Identity.Auditing;
using Identity.Domain.Roles;
using Npgsql;
using Payments.Domain.Payments;
using Ticketing.Application.Orders;
using Xunit;

namespace Api.IntegrationTests.Ticketing;

/// <summary>UF-005 Lipa Mdogo Mdogo and UF-006 transfers, plus naming the attendee of a ticket.</summary>
[Collection(IdentityApiCollection.Name)]
public sealed class HolderAndInstallmentHttpTests(IdentityAuditApiFixture fixture)
{
    private readonly TicketingApi _api = new(fixture);

    [Fact]
    public async Task LipaMdogoMdogo_DepositReservesWithoutQr_BalanceActivatesTickets()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.AdminAsync(client);
        var sale = await TicketingApi.PublishedEventAsync(client, admin, capacity: 10, price: 1001m);
        var customer = await _api.CustomerAsync(client);

        using var placed = await TicketingApi.PlaceOrderAsync(client, customer, Plan(sale, 3, "LipaMdogoMdogo"), Guid.NewGuid().ToString("N"));
        var order = await TicketingApi.ReadAsync(placed, HttpStatusCode.Created);
        var orderId = order.GetProperty("id").GetGuid();

        Assert.Equal("LipaMdogoMdogo", order.GetProperty("paymentPlan").GetString());
        // KES 3,003 of tickets plus 3 x KES 50 fee; the deposit is half the tickets (rounded up) plus the whole fee.
        Assert.Equal(3153m, order.GetProperty("total").GetDecimal());
        Assert.Equal(1652m, order.GetProperty("amountDueNow").GetDecimal());
        Assert.NotEqual(JsonValueKind.Null, order.GetProperty("balanceDueAt").ValueKind);

        var deposit = Assert.Single(fixture.PaymentProvider.PromptsTo(customer.Phone));
        Assert.Equal(1652m, deposit.Request.Amount);

        await _api.CompletePromptAsync(client, deposit, ProviderTransactionStatus.Success);
        var partial = await TicketingApi.WaitForOrderAsync(client, customer, orderId, "PartiallyPaid");
        Assert.Equal(1501m, partial.GetProperty("amountDueNow").GetDecimal());
        Assert.Equal((10, 0, 3), await _api.TierBucketAsync(sale.TierId));

        var reserved = await MyTicketsAsync(client, customer, sale);
        Assert.Equal(3, reserved.Count);
        Assert.All(reserved, x =>
        {
            Assert.Equal("AwaitingBalance", x.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, x.GetProperty("qrPayload").ValueKind);
        });

        // At the door before the balance is paid: NotFullyPaid.
        var door = await _api.AdminAsync(client, IdentityRole.Support);
        var early = await TicketingApi.SendAsync(client, HttpMethod.Post, "/api/ops/check-in/manual", door,
            new { eventDateId = sale.DateId, code = reserved[0].GetProperty("code").GetString(), ticketId = (Guid?)null }, HttpStatusCode.OK);
        Assert.Equal("NotFullyPaid", early.GetProperty("outcome").GetString());

        // Pay the balance: a second prompt for the rest, as an installment.
        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/client/orders/{orderId}/payments", customer, new { payerPhone = (string?)null }, HttpStatusCode.OK);
        var balance = fixture.PaymentProvider.PromptsTo(customer.Phone)[^1];
        Assert.Equal(1501m, balance.Request.Amount);
        Assert.StartsWith("Balance ", balance.Request.Description, StringComparison.Ordinal);

        await _api.CompletePromptAsync(client, balance, ProviderTransactionStatus.Success);
        var paid = await TicketingApi.WaitForOrderAsync(client, customer, orderId, "Paid");
        Assert.Equal(3153m, paid.GetProperty("amountPaid").GetDecimal());

        var active = await MyTicketsAsync(client, customer, sale);
        Assert.All(active, x => Assert.StartsWith("KH1.", x.GetProperty("qrPayload").GetString(), StringComparison.Ordinal));
        Assert.Contains(fixture.SmsGateway.SentTo(customer.Phone), x => x.Message.StartsWith("Fully paid", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LipaMdogoMdogo_OnlyWhenTheEventOffersIt()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.AdminAsync(client);
        var sale = await TicketingApi.PublishedEventAsync(client, admin, lipaMdogoMdogo: false);
        var customer = await _api.CustomerAsync(client);

        using var placed = await TicketingApi.PlaceOrderAsync(client, customer, Plan(sale, 1, "LipaMdogoMdogo"), Guid.NewGuid().ToString("N"));
        Assert.Equal("Ticketing.LipaMdogoMdogoUnavailable", (await TicketingApi.ReadAsync(placed, HttpStatusCode.Conflict)).GetProperty("errorKey").GetString());
    }

    [Fact]
    public async Task UnpaidBalance_IsRemindedOnce_ThenCancelledAndSeatsReturn()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.AdminAsync(client);
        var sale = await TicketingApi.PublishedEventAsync(client, admin, capacity: 4);
        var customer = await _api.CustomerAsync(client);

        using var placed = await TicketingApi.PlaceOrderAsync(client, customer, Plan(sale, 2, "LipaMdogoMdogo"), Guid.NewGuid().ToString("N"));
        var orderId = (await TicketingApi.ReadAsync(placed, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        await _api.CompletePromptAsync(client, Assert.Single(fixture.PaymentProvider.PromptsTo(customer.Phone)), ProviderTransactionStatus.Success);
        await TicketingApi.WaitForOrderAsync(client, customer, orderId, "PartiallyPaid");

        // Due in 2 days: the "3 days before" reminder is due, once.
        await SetBalanceDueAsync(orderId, "now() + interval '2 days'");
        await _api.RunTenantJobAsync<BalanceRemindersJob>();
        await _api.RunTenantJobAsync<BalanceRemindersJob>();
        Assert.Single(fixture.SmsGateway.SentTo(customer.Phone), x => x.Message.StartsWith("Reminder", StringComparison.Ordinal));

        // Two days overdue: cancelled, tickets voided, seats back on sale.
        await SetBalanceDueAsync(orderId, "now() - interval '2 days'");
        await _api.RunTenantJobAsync<BalanceRemindersJob>();

        await TicketingApi.WaitForOrderAsync(client, customer, orderId, "Cancelled");
        Assert.Empty(await MyTicketsAsync(client, customer, sale));
        Assert.Equal((4, 0, 0), await _api.TierBucketAsync(sale.TierId));
        Assert.Contains(fixture.SmsGateway.SentTo(customer.Phone), x => x.Message.Contains("cancelled", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Transfer_MovesTheTicket_KillsTheOldQr_AndRespectsTheAllowance()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.AdminAsync(client);
        var sale = await TicketingApi.PublishedEventAsync(client, admin);
        var sender = await _api.CustomerAsync(client);
        var ticket = (await _api.BuyAsync(client, sender, sale, 1))[0];
        var ticketId = ticket.GetProperty("id").GetGuid();
        var oldQr = ticket.GetProperty("qrPayload").GetString();

        var recipientPhone = IdentityApi.NewKenyanPhone();

        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/client/tickets/{ticketId}/transfer", sender,
            new { toPhone = sender.Phone, toName = "Me" }, HttpStatusCode.BadRequest);

        var moved = await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/client/tickets/{ticketId}/transfer", sender,
            new { toPhone = "0" + recipientPhone[4..], toName = "Achieng Otieno" }, HttpStatusCode.OK);
        Assert.Equal(recipientPhone[^3..], moved.GetProperty("toPhoneLast3").GetString());

        Assert.Empty(await MyTicketsAsync(client, sender, sale));
        Assert.Contains(fixture.SmsGateway.SentTo(recipientPhone), x => x.Message.Contains("sent you a ticket", StringComparison.Ordinal));

        // The recipient signs in with that number and has it, with a new QR; the sender's screenshot is dead.
        var recipient = await _api.CustomerAsync(client, recipientPhone);
        var theirs = Assert.Single(await MyTicketsAsync(client, recipient, sale));
        Assert.Equal("Achieng Otieno", theirs.GetProperty("holderName").GetString());
        Assert.NotEqual(oldQr, theirs.GetProperty("qrPayload").GetString());

        var door = await _api.AdminAsync(client, IdentityRole.Support);
        var stale = await TicketingApi.SendAsync(client, HttpMethod.Post, "/api/ops/check-in/scan", door, new { eventDateId = sale.DateId, payload = oldQr }, HttpStatusCode.OK);
        Assert.Equal("Invalid", stale.GetProperty("outcome").GetString());

        // One transfer per ticket (event policy): the recipient cannot pass it on, and the sender no longer can.
        using (var again = await IdentityApi.CallAsync(client, HttpMethod.Post, $"/api/client/tickets/{ticketId}/transfer", new { toPhone = IdentityApi.NewKenyanPhone(), toName = "X" }, recipient.AccessToken))
        {
            Assert.Equal("Ticketing.TransferLimitReached", (await TicketingApi.ReadAsync(again, HttpStatusCode.Conflict)).GetProperty("errorKey").GetString());
        }

        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/client/tickets/{ticketId}/transfer", sender,
            new { toPhone = IdentityApi.NewKenyanPhone(), toName = "X" }, HttpStatusCode.NotFound);

        var fresh = await TicketingApi.SendAsync(client, HttpMethod.Post, "/api/ops/check-in/scan", door,
            new { eventDateId = sale.DateId, payload = theirs.GetProperty("qrPayload").GetString() }, HttpStatusCode.OK);
        Assert.Equal("Admitted", fresh.GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task AttendeeName_ShowsOnTheTicketAndAtTheDoor()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.AdminAsync(client);
        var sale = await TicketingApi.PublishedEventAsync(client, admin);
        var customer = await _api.CustomerAsync(client);
        var ticket = (await _api.BuyAsync(client, customer, sale, 1))[0];
        var ticketId = ticket.GetProperty("id").GetGuid();

        var named = await TicketingApi.SendAsync(client, HttpMethod.Put, $"/api/client/tickets/{ticketId}/attendee", customer, new { name = "  Baraka  " }, HttpStatusCode.OK);
        Assert.Equal("Baraka", named.GetProperty("attendeeName").GetString());

        var door = await _api.AdminAsync(client, IdentityRole.Support);
        var scan = await TicketingApi.SendAsync(client, HttpMethod.Post, "/api/ops/check-in/scan", door,
            new { eventDateId = sale.DateId, payload = ticket.GetProperty("qrPayload").GetString() }, HttpStatusCode.OK);
        Assert.Equal("Baraka", scan.GetProperty("attendeeName").GetString());

        // Someone else's ticket: not found.
        var stranger = await _api.CustomerAsync(client);
        await TicketingApi.SendAsync(client, HttpMethod.Put, $"/api/client/tickets/{ticketId}/attendee", stranger, new { name = "X" }, HttpStatusCode.NotFound);
    }

    private static object Plan(TicketingApi.SaleEvent sale, int quantity, string plan) =>
        new { eventSlug = sale.Slug, items = new[] { new { tierId = sale.TierId, quantity } }, paymentPlan = plan };

    private static async Task<List<JsonElement>> MyTicketsAsync(HttpClient client, IdentityLogin customer, TicketingApi.SaleEvent sale)
    {
        var list = await TicketingApi.SendAsync(client, HttpMethod.Get, "/api/client/tickets", customer, null, HttpStatusCode.OK);
        return [.. list.EnumerateArray().Where(x => x.GetProperty("eventId").GetGuid() == sale.EventId)];
    }

    private async Task SetBalanceDueAsync(Guid orderId, string sqlInstant)
    {
        await using var connection = await _api.OpenTenantAsync();
#pragma warning disable CA2100 // Test-only SQL with constant expressions.
        await using var command = new NpgsqlCommand($"""UPDATE ticketing."order" SET "BalanceDueAt" = {sqlInstant} WHERE "Id" = @id""", connection);
#pragma warning restore CA2100
        command.Parameters.AddWithValue("id", orderId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }
}
