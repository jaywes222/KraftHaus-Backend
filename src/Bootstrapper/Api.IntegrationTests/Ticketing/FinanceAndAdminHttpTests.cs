using System.Net;
using System.Text.Json;
using Api.IntegrationTests.Identity.Auditing;
using Identity.Domain.Authorization;
using Identity.Domain.Roles;
using Npgsql;
using Payments.Domain.Common;
using Payments.Domain.Payments;
using Ticketing.Application.Waitlist;
using Ticketing.Domain.Common;
using Xunit;

namespace Api.IntegrationTests.Ticketing;

/// <summary>
///     Finance and admin follow-up: reviewing mismatched payments, settlement records (BR-025), totals, resolving flagged
///     orders, voiding tickets, order lookups, the waitlist resend job and the permission catalog.
/// </summary>
[Collection(IdentityApiCollection.Name)]
public sealed class FinanceAndAdminHttpTests(IdentityAuditApiFixture fixture)
{
    private readonly TicketingApi _api = new(fixture);

    [Fact]
    public async Task FinanceAcceptsAMismatchedPayment_AndTheOrderIsFulfilled()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.AdminAsync(client);
        var finance = await _api.AdminAsync(client, IdentityRole.Finance);
        var sale = await TicketingApi.PublishedEventAsync(client, admin, price: 1000m);
        var customer = await _api.CustomerAsync(client);

        var (orderId, paymentId) = await PayWrongAmountAsync(client, customer, sale, 1001m);

        // A note is required; then the payment counts and the order gets its tickets.
        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/payments/{paymentId}/review", finance,
            new { decision = "AcceptAsPaid", note = "" }, HttpStatusCode.BadRequest);
        var reviewed = await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/payments/{paymentId}/review", finance,
            new { decision = "AcceptAsPaid", note = "Customer paid 1 KES extra; kept." }, HttpStatusCode.OK);
        Assert.Equal("Succeeded", reviewed.GetProperty("status").GetString());
        Assert.Equal("AcceptAsPaid", reviewed.GetProperty("reviewDecision").GetString());

        var paid = await TicketingApi.WaitForOrderAsync(client, customer, orderId, "Paid");
        Assert.Equal(1, paid.GetProperty("ticketsIssued").GetInt32());

        // Reviewing twice is refused.
        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/payments/{paymentId}/review", finance,
            new { decision = "Refunded", note = "again" }, HttpStatusCode.Conflict);

        // Settlement (BR-025) is separate from the payment status.
        var unsettled = await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/admin/payments/{paymentId}", finance, null, HttpStatusCode.OK);
        Assert.Equal("Unsettled", unsettled.GetProperty("settlement").GetString());

        await TicketingApi.SendAsync(client, HttpMethod.Put, $"/api/admin/payments/{paymentId}/settlement", finance,
            new { status = "Settled", reference = (string?)null, settledAt = (DateTimeOffset?)null, note = (string?)null }, HttpStatusCode.BadRequest);
        var settled = await TicketingApi.SendAsync(client, HttpMethod.Put, $"/api/admin/payments/{paymentId}/settlement", finance,
            new { status = "Settled", reference = "PP-SETTLE-0042", settledAt = (DateTimeOffset?)null, note = "Weekly payout" }, HttpStatusCode.OK);
        Assert.Equal("Settled", settled.GetProperty("settlement").GetString());
        Assert.Equal("PP-SETTLE-0042", settled.GetProperty("settlementReference").GetString());

        var summary = await TicketingApi.SendAsync(client, HttpMethod.Get, "/api/admin/payments/summary", finance, null, HttpStatusCode.OK);
        Assert.True(summary.GetProperty("settled").GetDecimal() >= 1000m);
        Assert.True(summary.GetProperty("collected").GetDecimal() >= summary.GetProperty("settled").GetDecimal());
    }

    [Fact]
    public async Task FinanceRefundsAMismatchedPayment_AndTheCustomerCanPayAgain()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.AdminAsync(client);
        var finance = await _api.AdminAsync(client, IdentityRole.Finance);
        var sale = await TicketingApi.PublishedEventAsync(client, admin, price: 1000m);
        var customer = await _api.CustomerAsync(client);

        var (orderId, paymentId) = await PayWrongAmountAsync(client, customer, sale, 10m);

        var refunded = await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/payments/{paymentId}/review", finance,
            new { decision = "Refunded", note = "Reversed 10 KES in PalPluss." }, HttpStatusCode.OK);
        Assert.Equal("Refunded", refunded.GetProperty("status").GetString());

        // Settlement only applies to successful payments.
        await TicketingApi.SendAsync(client, HttpMethod.Put, $"/api/admin/payments/{paymentId}/settlement", finance,
            new { status = "Settled", reference = "X", settledAt = (DateTimeOffset?)null, note = (string?)null }, HttpStatusCode.Conflict);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        JsonElement order;
        do
        {
            order = await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/client/orders/{orderId}", customer, null, HttpStatusCode.OK);
            await Task.Delay(200);
        }
        while (order.GetProperty("lastPaymentFailure").ValueKind == JsonValueKind.Null && DateTimeOffset.UtcNow < deadline);

        Assert.Equal("Refunded", order.GetProperty("lastPaymentFailure").GetString());

        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/client/orders/{orderId}/payments", customer, new { payerPhone = (string?)null }, HttpStatusCode.OK);
        await _api.CompletePromptAsync(client, fixture.PaymentProvider.PromptsTo(customer.Phone)[^1], ProviderTransactionStatus.Success);
        await TicketingApi.WaitForOrderAsync(client, customer, orderId, "Paid");
    }

    [Fact]
    public async Task ReceiptFromTheCallback_FillsInWhenTheLookupHasNone()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.AdminAsync(client);
        var sale = await TicketingApi.PublishedEventAsync(client, admin);
        var customer = await _api.CustomerAsync(client);

        using var placed = await TicketingApi.PlaceOrderAsync(client, customer, Items(sale, 1), Guid.NewGuid().ToString("N"));
        var orderId = (await TicketingApi.ReadAsync(placed, HttpStatusCode.Created)).GetProperty("id").GetGuid();

        fixture.PaymentProvider.OmitReceipts = true;
        try
        {
            await _api.CompletePromptAsync(client, Assert.Single(fixture.PaymentProvider.PromptsTo(customer.Phone)),
                ProviderTransactionStatus.Success, callbackReceipt: "QKA1B2C3D4");
        }
        finally
        {
            fixture.PaymentProvider.OmitReceipts = false;
        }

        var paid = await TicketingApi.WaitForOrderAsync(client, customer, orderId, "Paid");
        Assert.Equal("QKA1B2C3D4", paid.GetProperty("payment").GetProperty("mpesaReceipt").GetString());
    }

    [Fact]
    public async Task AdminVoidsATicket_ResolvesFlaggedOrders_AndCustomersSeeTheirOrders()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.AdminAsync(client);
        var sale = await TicketingApi.PublishedEventAsync(client, admin);
        var customer = await _api.CustomerAsync(client);
        var tickets = await _api.BuyAsync(client, customer, sale, 2);

        var mine = await TicketingApi.SendAsync(client, HttpMethod.Get, "/api/client/orders", customer, null, HttpStatusCode.OK);
        var order = Assert.Single(mine.EnumerateArray());
        var orderId = order.GetProperty("id").GetGuid();
        Assert.Equal("Paid", order.GetProperty("status").GetString());

        var adminTickets = await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/admin/orders/{orderId}/tickets", admin, null, HttpStatusCode.OK);
        Assert.Equal(2, adminTickets.GetArrayLength());
        Assert.DoesNotContain(customer.Phone[4..], adminTickets.ToString(), StringComparison.Ordinal);

        var ticketId = tickets[0].GetProperty("id").GetGuid();
        var voided = await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/tickets/{ticketId}/void", admin, new { reason = "Reported lost; reissued" }, HttpStatusCode.OK);
        Assert.Equal("Void", voided.GetProperty("status").GetString());

        var door = await _api.AdminAsync(client, IdentityRole.Support);
        var scan = await TicketingApi.SendAsync(client, HttpMethod.Post, "/api/ops/check-in/scan", door,
            new { eventDateId = sale.DateId, payload = tickets[0].GetProperty("qrPayload").GetString() }, HttpStatusCode.OK);
        Assert.Equal("Invalid", scan.GetProperty("outcome").GetString());

        // Finance has no ticket permissions: cannot void. Support cannot export the waitlist (personal data).
        var finance = await _api.AdminAsync(client, IdentityRole.Finance);
        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/tickets/{tickets[1].GetProperty("id").GetGuid()}/void", finance, new { reason = "x" }, HttpStatusCode.Forbidden);
        await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/admin/events/{sale.EventId}/waitlist", door, null, HttpStatusCode.Forbidden);
        await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/admin/events/{sale.EventId}/waitlist", admin, null, HttpStatusCode.OK);

        // An order with nothing to resolve.
        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/orders/{orderId}/resolve", admin, new { resolution = "n/a" }, HttpStatusCode.Conflict);

        // Flag the order the way a second paid prompt would, then resolve it.
        await using (var connection = await _api.OpenTenantAsync())
        await using (var flag = new NpgsqlCommand("""UPDATE ticketing."order" SET "AttentionReason" = 'Second payment' WHERE "Id" = @id""", connection))
        {
            flag.Parameters.AddWithValue("id", orderId);
            await flag.ExecuteNonQueryAsync();
        }

        var open = await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/admin/events/{sale.EventId}/orders?needsAction=true", admin, null, HttpStatusCode.OK);
        Assert.Single(open.EnumerateArray());

        var resolved = await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/orders/{orderId}/resolve", admin,
            new { resolution = "Refunded the second payment in PalPluss (ref RF-12)." }, HttpStatusCode.OK);
        Assert.False(resolved.GetProperty("needsAction").GetBoolean());

        var after = await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/admin/events/{sale.EventId}/orders?needsAction=true", admin, null, HttpStatusCode.OK);
        Assert.Equal(0, after.GetArrayLength());
    }

    [Fact]
    public async Task WaitlistConfirmation_IsResentAfterAnOutage()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.AdminAsync(client);
        var sale = await TicketingApi.PublishedEventAsync(client, admin);
        var phone = IdentityApi.NewKenyanPhone();

        fixture.SmsGateway.FailNext = true;
        using (var join = await IdentityApi.CallAsync(client, HttpMethod.Post, "/api/client/waitlist", new
               {
                   eventSlug = sale.Slug, name = "Wanjiru Kamau", phone, age = 27, gender = "woman", location = "Kilimani",
                   email = (string?)null, privacyAccepted = true, privacyPolicyVersion = "2026-10-01", marketingConsent = false
               }))
        {
            Assert.False((await TicketingApi.ReadAsync(join, HttpStatusCode.Created)).GetProperty("smsSent").GetBoolean());
        }

        await using (var connection = await _api.OpenTenantAsync())
        await using (var age = new NpgsqlCommand("""UPDATE ticketing.waitlist_entry SET "JoinedAt" = now() - interval '10 minutes' WHERE "Phone" = @phone""", connection))
        {
            age.Parameters.AddWithValue("phone", phone);
            await age.ExecuteNonQueryAsync();
        }

        await _api.RunTenantJobAsync<WaitlistResendJob>();
        await _api.RunTenantJobAsync<WaitlistResendJob>();

        Assert.Single(fixture.SmsGateway.SentTo(phone));
    }

    [Fact]
    public void TicketingAndPaymentsPermissions_AreInTheIdentityCatalog()
    {
        Assert.Subset(IdentityPermission.All.ToHashSet(StringComparer.Ordinal), TicketingPermission.All.ToHashSet(StringComparer.Ordinal));
        Assert.Subset(IdentityPermission.All.ToHashSet(StringComparer.Ordinal), PaymentsPermission.All.ToHashSet(StringComparer.Ordinal));

        Assert.True(IdentityPermissionMatrix.HasPermission(IdentityRole.Support, TicketingPermission.CheckIn));
        Assert.True(IdentityPermissionMatrix.HasPermission(IdentityRole.Finance, PaymentsPermission.Manage));
        Assert.False(IdentityPermissionMatrix.HasPermission(IdentityRole.Support, TicketingPermission.ExportWaitlist));
        Assert.True(IdentityPermissionMatrix.HasPermission(IdentityRole.TenantAdmin, TicketingPermission.ExportWaitlist));
    }

    private async Task<(Guid OrderId, Guid PaymentId)> PayWrongAmountAsync(HttpClient client, IdentityLogin customer, TicketingApi.SaleEvent sale, decimal reported)
    {
        using var placed = await TicketingApi.PlaceOrderAsync(client, customer, Items(sale, 1), Guid.NewGuid().ToString("N"));
        var order = await TicketingApi.ReadAsync(placed, HttpStatusCode.Created);
        var orderId = order.GetProperty("id").GetGuid();
        var paymentId = order.GetProperty("payment").GetProperty("paymentId").GetGuid();

        await _api.CompletePromptAsync(client, fixture.PaymentProvider.PromptsTo(customer.Phone)[^1], ProviderTransactionStatus.Success, reportedAmount: reported);

        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var view = await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/client/orders/{orderId}", customer, null, HttpStatusCode.OK);
            if (view.GetProperty("payment").GetProperty("status").GetString() == "NeedsReview")
            {
                return (orderId, paymentId);
            }

            await Task.Delay(100);
        }

        Assert.Fail("The payment never reached NeedsReview.");
        return default;
    }

    private static object Items(TicketingApi.SaleEvent sale, int quantity) =>
        new { eventSlug = sale.Slug, items = new[] { new { tierId = sale.TierId, quantity } } };
}
