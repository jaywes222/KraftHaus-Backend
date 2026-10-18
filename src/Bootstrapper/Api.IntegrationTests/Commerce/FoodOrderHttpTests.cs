using System.Net;
using System.Text.Json;
using Api.IntegrationTests.Identity.Auditing;
using Api.IntegrationTests.Ticketing;
using Commerce.Application.Orders;
using Identity.Domain.Roles;
using Npgsql;
using Payments.Domain.Payments;
using Xunit;

namespace Api.IntegrationTests.Commerce;

/// <summary>
///     Food and drink end to end: the ticket gate (O-04), split carts with one prompt per Kraft into its own till (D-03),
///     the fee split, stock, the kitchen board and pickup (UF-014/015, BR-026), items running out after payment (O-05),
///     reservations, hold expiry and late money.
/// </summary>
[Collection(IdentityApiCollection.Name)]
public sealed class FoodOrderHttpTests(IdentityAuditApiFixture fixture)
{
    private static readonly string[] ExpectedKraftOrder = ["Juice Bar", "Mama Pima"];

    private readonly CommerceApi _api = new(fixture);

    [Fact]
    public async Task ASplitCartIsPaidKraftByKraftCookedAndCollected()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.Ticketing.AdminAsync(client);
        var finance = await _api.Ticketing.AdminAsync(client, IdentityRole.Finance);
        var sale = await TicketingApi.PublishedEventAsync(client, admin, startsIn: TimeSpan.FromMinutes(30));

        var grill = await _api.SellingKraftAsync(client, admin, finance, "Mama Pima", sale.EventId, commission: 10m);
        var juice = await _api.SellingKraftAsync(client, admin, finance, "Juice Bar", sale.EventId, commission: 0m);
        var nyama = await CommerceApi.MenuItemAsync(client, admin, grill, sale.EventId, "Nyama choma", 800m, liveStock: 3);
        var tusker = await CommerceApi.MenuItemAsync(client, admin, grill, sale.EventId, "Tusker", 300m, category: "Drink");
        var passion = await CommerceApi.MenuItemAsync(client, admin, juice, sale.EventId, "Passion juice", 200m);

        var menu = await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/client/events/{sale.Slug}/menu", admin, null, HttpStatusCode.OK);
        Assert.True(menu.GetProperty("liveOrderingOpen").GetBoolean());
        Assert.False(menu.GetProperty("reservationsOpen").GetBoolean());
        Assert.Equal(2, menu.GetProperty("krafts").GetArrayLength());
        var tuskerItem = menu.GetProperty("krafts").EnumerateArray().SelectMany(x => x.GetProperty("items").EnumerateArray())
            .Single(x => x.GetProperty("id").GetGuid() == tusker);
        Assert.True(tuskerItem.GetProperty("ageRestricted").GetBoolean());

        var cart = new
        {
            eventSlug = sale.Slug,
            kind = "Live",
            items = new[] { new { menuItemId = nyama, quantity = 2 }, new { menuItemId = tusker, quantity = 1 }, new { menuItemId = passion, quantity = 1 } },
            ageConfirmed = true,
            payerPhone = (string?)null
        };

        // O-04: no ticket, no food.
        var customer = await _api.Ticketing.CustomerAsync(client);
        using (var noTicket = await CommerceApi.CheckoutAsync(client, customer, cart, Guid.NewGuid().ToString("N")))
        {
            var error = await TicketingApi.ReadAsync(noTicket, HttpStatusCode.Forbidden);
            Assert.Equal("Commerce.TicketRequired", error.GetProperty("errorKey").GetString());
        }

        await _api.Ticketing.BuyAsync(client, customer, sale, 1);

        using (var noKey = await CommerceApi.CheckoutAsync(client, customer, cart, null))
        {
            await TicketingApi.ReadAsync(noKey, HttpStatusCode.BadRequest);
        }

        // This customer's checkout holds 2 of the 3 Nyama portions (never paid in this test).
        using (var held = await CommerceApi.CheckoutAsync(client, customer, cart, Guid.NewGuid().ToString("N")))
        {
            Assert.Equal(HttpStatusCode.Created, held.StatusCode);
        }

        var buyer = await _api.Ticketing.CustomerAsync(client);
        await _api.Ticketing.BuyAsync(client, buyer, sale, 1);

        var withoutAge = new { cart.eventSlug, cart.kind, cart.items, ageConfirmed = false, cart.payerPhone };
        using (var refused = await CommerceApi.CheckoutAsync(client, buyer, withoutAge, Guid.NewGuid().ToString("N")))
        {
            var error = await TicketingApi.ReadAsync(refused, HttpStatusCode.BadRequest);
            Assert.Equal("Commerce.AgeConfirmationRequired", error.GetProperty("errorKey").GetString());
        }

        // Only 1 Nyama left now (the first customer holds 2 of 3): the whole checkout is refused.
        using (var soldOut = await CommerceApi.CheckoutAsync(client, buyer, cart, Guid.NewGuid().ToString("N")))
        {
            var error = await TicketingApi.ReadAsync(soldOut, HttpStatusCode.Conflict);
            Assert.Equal("Commerce.SoldOut", error.GetProperty("errorKey").GetString());
            Assert.Contains(error.GetProperty("errors").EnumerateArray(), x => x.GetString()!.StartsWith("Mama Pima: Nyama choma", StringComparison.Ordinal));
        }

        var key = Guid.NewGuid().ToString("N");
        var smaller = new { cart.eventSlug, cart.kind, items = new[] { new { menuItemId = nyama, quantity = 1 }, new { menuItemId = tusker, quantity = 1 }, new { menuItemId = passion, quantity = 1 } }, ageConfirmed = true, cart.payerPhone };
        JsonElement checkout;
        using (var placed = await CommerceApi.CheckoutAsync(client, buyer, smaller, key))
        {
            checkout = await TicketingApi.ReadAsync(placed, HttpStatusCode.Created);
        }

        // KES 1,300 of food plus the KES 30 service fee on each of the two Kraft orders.
        Assert.Equal(1360m, checkout.GetProperty("total").GetDecimal());
        Assert.Equal(2, checkout.GetProperty("promptsToExpect").GetInt32());
        var orders = checkout.GetProperty("orders").EnumerateArray().ToList();
        Assert.Equal(ExpectedKraftOrder, orders.Select(x => x.GetProperty("kraftName").GetString()));
        Assert.All(orders, x => Assert.Equal(JsonValueKind.Null, x.GetProperty("pickupCode").ValueKind));

        // Same key: same checkout, nothing new held.
        using (var repeat = await CommerceApi.CheckoutAsync(client, buyer, smaller, key))
        {
            var again = await TicketingApi.ReadAsync(repeat, HttpStatusCode.Created);
            Assert.Equal(checkout.GetProperty("checkoutGroupId").GetGuid(), again.GetProperty("checkoutGroupId").GetGuid());
        }

        var juiceOrder = orders[0].GetProperty("id").GetGuid();
        var grillOrder = orders[1].GetProperty("id").GetGuid();
        Assert.Equal(juiceOrder, checkout.GetProperty("nextOrderToPay").GetGuid());

        // One prompt at a time, into each Kraft's own till channel.
        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/client/kraft-orders/{juiceOrder}/pay", buyer, new { payerPhone = (string?)null }, HttpStatusCode.OK);
        var juicePrompt = fixture.PaymentProvider.PromptsTo(buyer.Phone)[^1];
        Assert.Equal(230m, juicePrompt.Request.Amount);
        Assert.NotNull(juicePrompt.Request.ChannelId);
        var busy = await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/client/kraft-orders/{grillOrder}/pay", buyer, new { payerPhone = (string?)null }, HttpStatusCode.Conflict);
        Assert.Equal("Commerce.PaymentInProgress", busy.GetProperty("errorKey").GetString());

        await _api.Ticketing.CompletePromptAsync(client, juicePrompt, ProviderTransactionStatus.Success);
        var juicePaid = await CommerceApi.WaitForKraftOrderAsync(client, buyer, juiceOrder, "Paid");
        var juiceCode = juicePaid.GetProperty("pickupCode").GetString()!;

        var grillPaid = await _api.PayAsync(client, buyer, grillOrder);
        var grillPrompt = fixture.PaymentProvider.PromptsTo(buyer.Phone)[^1];
        Assert.Equal(1130m, grillPrompt.Request.Amount);
        Assert.NotEqual(juicePrompt.Request.ChannelId, grillPrompt.Request.ChannelId);
        Assert.Contains(fixture.SmsGateway.SentTo(buyer.Phone), x => x.Message.Contains(grillPaid.GetProperty("reference").GetString()!, StringComparison.Ordinal));

        // Fee split fixed at confirmation (10% of 1100).
        var grillAdmin = await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/admin/kraft-orders/{grillOrder}", finance, null, HttpStatusCode.OK);
        // 10% commission on the KES 1,100 subtotal plus the KES 30 service fee.
        Assert.Equal(140m, grillAdmin.GetProperty("platformFee").GetDecimal());
        Assert.Equal(30m, grillAdmin.GetProperty("serviceFee").GetDecimal());
        Assert.Equal(990m, grillAdmin.GetProperty("kraftRevenue").GetDecimal());
        var sales = await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/admin/events/{sale.EventId}/kraft-sales", finance, null, HttpStatusCode.OK);
        Assert.Equal(140m, sales.EnumerateArray().Single(x => x.GetProperty("kraftId").GetGuid() == grill).GetProperty("platformFees").GetDecimal());

        // Kitchen board: own Kraft only (BR-026).
        var grillStaff = await _api.KraftStaffAsync(client, grill, IdentityRole.KraftStaff);
        var board = await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/ops/krafts/{grill}/orders?eventId={sale.EventId}", grillStaff, null, HttpStatusCode.OK);
        Assert.Contains(board.EnumerateArray(), x => x.GetProperty("id").GetGuid() == grillOrder);
        Assert.DoesNotContain("Phone", board.GetRawText(), StringComparison.Ordinal);
        await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/ops/krafts/{juice}/orders", grillStaff, null, HttpStatusCode.Forbidden);
        await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/admin/krafts/{grill}/orders", grillStaff, null, HttpStatusCode.Forbidden);

        // Another Kraft's receipt is simply not found here.
        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/ops/krafts/{grill}/orders/collect", grillStaff, new { code = juiceCode }, HttpStatusCode.NotFound);

        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/ops/krafts/{grill}/orders/{grillOrder}/collect", grillStaff, null, HttpStatusCode.Conflict);
        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/ops/krafts/{grill}/orders/{grillOrder}/accept", grillStaff, null, HttpStatusCode.OK);
        var ready = await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/ops/krafts/{grill}/orders/{grillOrder}/ready", grillStaff, null, HttpStatusCode.OK);
        Assert.Equal("Ready", ready.GetProperty("status").GetString());
        Assert.Contains(fixture.SmsGateway.SentTo(buyer.Phone), x => x.Message.Contains("is ready", StringComparison.Ordinal));

        var grillCode = grillPaid.GetProperty("pickupCode").GetString()!;
        var collected = await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/ops/krafts/{grill}/orders/collect", grillStaff,
            new { code = $" {grillCode.Insert(4, "-")} " }, HttpStatusCode.OK);
        Assert.Equal("Collected", collected.GetProperty("status").GetString());
        var twice = await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/ops/krafts/{grill}/orders/collect", grillStaff, new { code = grillCode }, HttpStatusCode.Conflict);
        Assert.Equal("Commerce.AlreadyCollected", twice.GetProperty("errorKey").GetString());

        // O-05: the juice ran out after payment. The line's money is owed back and Finance closes it by hand.
        var juiceStaff = await _api.KraftStaffAsync(client, juice, IdentityRole.KraftStaff);
        var line = juicePaid.GetProperty("lines")[0].GetProperty("id").GetGuid();
        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/ops/krafts/{juice}/orders/{juiceOrder}/lines/{line}/unavailable", juiceStaff,
            new { note = "", switchOffItem = true }, HttpStatusCode.BadRequest);
        var flagged = await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/ops/krafts/{juice}/orders/{juiceOrder}/lines/{line}/unavailable", juiceStaff,
            new { note = "Out of passion fruit", switchOffItem = true }, HttpStatusCode.OK);
        Assert.True(flagged.GetProperty("needsAction").GetBoolean());
        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/ops/krafts/{juice}/orders/{juiceOrder}/ready", juiceStaff, null, HttpStatusCode.Conflict);

        var queue = await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/admin/kraft-orders?eventId={sale.EventId}&needsAction=true", finance, null, HttpStatusCode.OK);
        var owed = queue.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == juiceOrder);
        Assert.Equal(200m, owed.GetProperty("amountToRefund").GetDecimal());
        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/kraft-orders/{juiceOrder}/resolve", finance,
            new { resolution = "Refunded KES 200 by M-Pesa reversal." }, HttpStatusCode.OK);

        menu = await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/client/events/{sale.Slug}/menu", admin, null, HttpStatusCode.OK);
        var passionItem = menu.GetProperty("krafts").EnumerateArray().SelectMany(x => x.GetProperty("items").EnumerateArray())
            .Single(x => x.GetProperty("id").GetGuid() == passion);
        Assert.True(passionItem.GetProperty("soldOut").GetBoolean());

        var grillMenu = await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/admin/krafts/{grill}/menu?eventId={sale.EventId}", grillStaff, null, HttpStatusCode.OK);
        var nyamaLevels = grillMenu.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == nyama);
        Assert.Equal(1, nyamaLevels.GetProperty("liveSold").GetInt32());
        Assert.Equal(2, nyamaLevels.GetProperty("liveHeld").GetInt32());
        Assert.Equal(0, nyamaLevels.GetProperty("liveLeft").GetInt32());
    }

    [Fact]
    public async Task LiveOrdersNeedATicketForTheDateThatIsOn()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.Ticketing.AdminAsync(client);
        var finance = await _api.Ticketing.AdminAsync(client, IdentityRole.Finance);
        var sale = await TicketingApi.PublishedEventAsync(client, admin, startsIn: TimeSpan.FromMinutes(30));
        var kraft = await _api.SellingKraftAsync(client, admin, finance, "Chapo Corner", sale.EventId);
        var chapo = await CommerceApi.MenuItemAsync(client, admin, kraft, sale.EventId, "Chapati", 50m);

        var customer = await _api.Ticketing.CustomerAsync(client);
        var ticketId = (await _api.Ticketing.BuyAsync(client, customer, sale, 1)).Single().GetProperty("id").GetGuid();
        var cart = new { eventSlug = sale.Slug, kind = "Live", items = new[] { new { menuItemId = chapo, quantity = 2 } }, ageConfirmed = false, payerPhone = (string?)null };

        // The same ticket, but for another date of the event: no live order today.
        await MoveTicketToDateAsync(ticketId, Guid.CreateVersion7());
        using (var otherDate = await CommerceApi.CheckoutAsync(client, customer, cart, Guid.NewGuid().ToString("N")))
        {
            var error = await TicketingApi.ReadAsync(otherDate, HttpStatusCode.Forbidden);
            Assert.Equal("Commerce.TicketRequired", error.GetProperty("errorKey").GetString());
        }

        await MoveTicketToDateAsync(ticketId, sale.DateId);
        using var today = await CommerceApi.CheckoutAsync(client, customer, cart, Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.Created, today.StatusCode);
    }

    private async Task MoveTicketToDateAsync(Guid ticketId, Guid dateId)
    {
        await using var connection = await _api.Ticketing.OpenTenantAsync();
        await using var command = new NpgsqlCommand("""UPDATE ticketing.ticket SET "EventDateId" = @date WHERE "Id" = @id""", connection);
        command.Parameters.AddWithValue("date", dateId);
        command.Parameters.AddWithValue("id", ticketId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task ReservationsComeFromTheirAllocationAndLateMoneyIsFlagged()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.Ticketing.AdminAsync(client);
        var finance = await _api.Ticketing.AdminAsync(client, IdentityRole.Finance);
        var sale = await TicketingApi.PublishedEventAsync(client, admin);
        var kraft = await _api.SellingKraftAsync(client, admin, finance, "Pilau House", sale.EventId);
        var pilau = await CommerceApi.MenuItemAsync(client, admin, kraft, sale.EventId, "Pilau box", 500m, liveStock: 0, reservationAllocation: 2);

        var first = await _api.Ticketing.CustomerAsync(client);
        var second = await _api.Ticketing.CustomerAsync(client);
        await _api.Ticketing.BuyAsync(client, first, sale, 1);
        await _api.Ticketing.BuyAsync(client, second, sale, 1);

        object Reserve(string kind, int quantity) => new
        {
            eventSlug = sale.Slug,
            kind,
            items = new[] { new { menuItemId = pilau, quantity } },
            ageConfirmed = false,
            payerPhone = (string?)null
        };

        using (var live = await CommerceApi.CheckoutAsync(client, first, Reserve("Live", 1), Guid.NewGuid().ToString("N")))
        {
            Assert.Equal("Commerce.LiveOrderingClosed", (await TicketingApi.ReadAsync(live, HttpStatusCode.Conflict)).GetProperty("errorKey").GetString());
        }

        using (var tooMany = await CommerceApi.CheckoutAsync(client, first, Reserve("Reservation", 3), Guid.NewGuid().ToString("N")))
        {
            await TicketingApi.ReadAsync(tooMany, HttpStatusCode.Conflict);
        }

        Guid orderId;
        using (var reserved = await CommerceApi.CheckoutAsync(client, first, Reserve("Reservation", 2), Guid.NewGuid().ToString("N")))
        {
            orderId = (await TicketingApi.ReadAsync(reserved, HttpStatusCode.Created)).GetProperty("orders")[0].GetProperty("id").GetGuid();
        }

        using (var none = await CommerceApi.CheckoutAsync(client, second, Reserve("Reservation", 1), Guid.NewGuid().ToString("N")))
        {
            await TicketingApi.ReadAsync(none, HttpStatusCode.Conflict);
        }

        // The first customer starts paying, then the hold (and its grace) runs out: the portions go back on sale.
        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/client/kraft-orders/{orderId}/pay", first, new { payerPhone = (string?)null }, HttpStatusCode.OK);
        var latePrompt = fixture.PaymentProvider.PromptsTo(first.Phone)[^1];
        await ExpireHoldAsync(orderId);
        await _api.Ticketing.RunTenantJobAsync<ReleaseExpiredKraftOrdersJob>();
        await CommerceApi.WaitForKraftOrderAsync(client, first, orderId, "Expired");

        Guid secondOrder;
        using (var taken = await CommerceApi.CheckoutAsync(client, second, Reserve("Reservation", 2), Guid.NewGuid().ToString("N")))
        {
            secondOrder = (await TicketingApi.ReadAsync(taken, HttpStatusCode.Created)).GetProperty("orders")[0].GetProperty("id").GetGuid();
        }

        var paidReservation = await _api.PayAsync(client, second, secondOrder);
        Assert.Equal("Reservation", paidReservation.GetProperty("kind").GetString());
        Assert.NotNull(paidReservation.GetProperty("pickupCode").GetString());

        // Late money for the expired order: the portions are gone, so Finance gets it.
        await _api.Ticketing.CompletePromptAsync(client, latePrompt, ProviderTransactionStatus.Success);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        JsonElement flagged = default;
        while (DateTimeOffset.UtcNow < deadline)
        {
            flagged = await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/admin/kraft-orders/{orderId}", finance, null, HttpStatusCode.OK);
            if (flagged.GetProperty("needsAction").GetBoolean())
            {
                break;
            }

            await Task.Delay(200);
        }

        Assert.True(flagged.GetProperty("needsAction").GetBoolean());
        Assert.Equal("Expired", flagged.GetProperty("status").GetString());
        Assert.Equal(1030m, flagged.GetProperty("amountToRefund").GetDecimal());
    }

    [Fact]
    public async Task TheKitchenHubAdmitsOnlyTheKraftsOwnStaff()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.Ticketing.AdminAsync(client);
        var sale = await TicketingApi.PublishedEventAsync(client, admin);
        var mine = await CommerceApi.CreateKraftAsync(client, admin, "Hub Kraft", sale.EventId);
        var other = await CommerceApi.CreateKraftAsync(client, admin, "Other Kraft", sale.EventId);
        var staff = await _api.KraftStaffAsync(client, mine, IdentityRole.KraftStaff);

        async Task<HttpStatusCode> NegotiateAsync(Guid kraftId, string? bearer, string? queryToken)
        {
            var path = $"/hubs/krafts/{kraftId}/kitchen/negotiate?negotiateVersion=1" + (queryToken is null ? string.Empty : $"&access_token={queryToken}");
            using var request = new HttpRequestMessage(HttpMethod.Post, path);
            if (bearer is not null)
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
            }

            using var response = await client.SendAsync(request);
            return response.StatusCode;
        }

        Assert.Equal(HttpStatusCode.OK, await NegotiateAsync(mine, staff.AccessToken, null));
        Assert.Equal(HttpStatusCode.OK, await NegotiateAsync(mine, null, staff.AccessToken));
        Assert.Equal(HttpStatusCode.Forbidden, await NegotiateAsync(other, staff.AccessToken, null));
        Assert.Equal(HttpStatusCode.Unauthorized, await NegotiateAsync(mine, null, null));
        Assert.Equal(HttpStatusCode.OK, await NegotiateAsync(other, admin.AccessToken, null));
    }

    private async Task ExpireHoldAsync(Guid orderId)
    {
        await using var connection = await _api.Ticketing.OpenTenantAsync();
        await using var command = new NpgsqlCommand(
            """UPDATE commerce.kraft_order SET "HoldExpiresAt" = now() - interval '1 hour' WHERE "Id" = @id""",
            connection);
        command.Parameters.AddWithValue("id", orderId);
        await command.ExecuteNonQueryAsync();
    }
}
