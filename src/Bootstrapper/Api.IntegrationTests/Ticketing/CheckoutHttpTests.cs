using System.Net;
using System.Text.Json;
using Api.IntegrationTests.Identity.Auditing;
using Identity.Domain.Roles;
using Npgsql;
using Payments.Application.Payments;
using Payments.Domain.Payments;
using Ticketing.Application.Orders;
using Xunit;

namespace Api.IntegrationTests.Ticketing;

/// <summary>
///     UF-003 end to end through the real host, a real tenant database, the tenant outbox and a scripted PalPluss:
///     order → hold → STK prompt → callback verified against the provider → tickets; plus sold out, retries, expiry,
///     late money, early access, idempotency and the Finance view.
/// </summary>
[Collection(IdentityApiCollection.Name)]
public sealed class CheckoutHttpTests(IdentityAuditApiFixture fixture)
{
    private readonly TicketingApi _api = new(fixture);

    [Fact]
    public async Task Purchase_PromptsMpesa_ThenIssuesTicketsOnConfirmedPayment()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.AdminAsync(client);
        var sale = await TicketingApi.PublishedEventAsync(client, admin, capacity: 10, price: 1500m, withCouple: true);
        var customer = await _api.CustomerAsync(client);
        var key = Guid.NewGuid().ToString("N");

        var body = new { eventSlug = sale.Slug, items = new object[] { new { tierId = sale.TierId, quantity = 2 }, new { offerId = sale.CoupleOfferId, quantity = 1 } } };

        using var placed = await TicketingApi.PlaceOrderAsync(client, customer, body, key);
        var order = await TicketingApi.ReadAsync(placed, HttpStatusCode.Created);
        var orderId = order.GetProperty("id").GetGuid();

        Assert.Equal("AwaitingPayment", order.GetProperty("status").GetString());
        // Four admissions (two singles and a Couple): KES 50 platform fee each on top.
        Assert.Equal(1500m * 2 + 2800m, order.GetProperty("subtotal").GetDecimal());
        Assert.Equal(200m, order.GetProperty("platformFee").GetDecimal());
        Assert.False(order.GetProperty("feeWaived").GetBoolean());
        Assert.Equal(6000m, order.GetProperty("total").GetDecimal());
        Assert.Equal(4, order.GetProperty("admissions").GetInt32());
        Assert.Equal("Pending", order.GetProperty("payment").GetProperty("status").GetString());

        // One prompt, for the order total, to the customer's phone, referencing the order.
        var prompt = Assert.Single(fixture.PaymentProvider.PromptsTo(customer.Phone));
        Assert.Equal(6000m, prompt.Request.Amount);
        Assert.Equal(order.GetProperty("reference").GetString(), prompt.Request.AccountReference);
        Assert.StartsWith("https://localhost/webhooks/palpluss/", prompt.Request.CallbackUrl.ToString(), StringComparison.Ordinal);

        Assert.Equal((10, 4, 0), await _api.TierBucketAsync(sale.TierId));

        // Same Idempotency-Key: the same order back, no second prompt.
        using (var repeat = await TicketingApi.PlaceOrderAsync(client, customer, body, key))
        {
            var again = await TicketingApi.ReadAsync(repeat, HttpStatusCode.Created);
            Assert.Equal(orderId, again.GetProperty("id").GetGuid());
            Assert.Single(fixture.PaymentProvider.PromptsTo(customer.Phone));
        }

        // PalPluss calls back; the outcome is read from PalPluss, delivered through the outbox.
        Assert.Equal(HttpStatusCode.OK, await _api.CompletePromptAsync(client, prompt, ProviderTransactionStatus.Success));
        var paid = await TicketingApi.WaitForOrderAsync(client, customer, orderId, "Paid");
        Assert.Equal(4, paid.GetProperty("ticketsIssued").GetInt32());
        Assert.StartsWith("TST", paid.GetProperty("payment").GetProperty("mpesaReceipt").GetString(), StringComparison.Ordinal);

        // A duplicate callback changes nothing.
        Assert.Equal(HttpStatusCode.OK, await _api.CompletePromptAsync(client, prompt, ProviderTransactionStatus.Success));

        var tickets = await TicketingApi.SendAsync(client, HttpMethod.Get, "/api/client/tickets", customer, null, HttpStatusCode.OK);
        var mine = tickets.EnumerateArray().Where(x => x.GetProperty("eventId").GetGuid() == sale.EventId).ToList();
        Assert.Equal(4, mine.Count);
        Assert.All(mine, x => Assert.Equal("Valid", x.GetProperty("status").GetString()));
        Assert.Equal(2, mine.Where(x => x.GetProperty("bundleId").ValueKind != JsonValueKind.Null).Select(x => x.GetProperty("bundleId").GetGuid()).Count());
        Assert.Equal(4, mine.Select(x => x.GetProperty("code").GetString()).Distinct().Count());

        Assert.Equal((10, 0, 4), await _api.TierBucketAsync(sale.TierId));
        Assert.Contains(fixture.SmsGateway.SentTo(customer.Phone), x => x.Message.Contains("4 tickets", StringComparison.Ordinal));

        await using var connection = await _api.OpenTenantAsync();
        await using var audit = new NpgsqlCommand(
            """SELECT count(*) FROM ticketing.audit_log WHERE "TargetId" = @id AND "Action" IN ('ticketing.order.placed','ticketing.order.paid')""",
            connection);
        audit.Parameters.AddWithValue("id", orderId.ToString("D"));
        Assert.Equal(2L, (long)(await audit.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task SoldOut_Returns409_AndHoldsNothing()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.AdminAsync(client);
        var sale = await TicketingApi.PublishedEventAsync(client, admin, capacity: 3);

        var first = await _api.CustomerAsync(client);
        using (var a = await TicketingApi.PlaceOrderAsync(client, first, Items(sale, 2), Guid.NewGuid().ToString("N")))
        {
            await TicketingApi.ReadAsync(a, HttpStatusCode.Created);
        }

        var second = await _api.CustomerAsync(client);
        using var b = await TicketingApi.PlaceOrderAsync(client, second, Items(sale, 2), Guid.NewGuid().ToString("N"));
        var problem = await TicketingApi.ReadAsync(b, HttpStatusCode.Conflict);

        Assert.Equal("Ticketing.SoldOut", problem.GetProperty("errorKey").GetString());
        Assert.Equal((3, 2, 0), await _api.TierBucketAsync(sale.TierId));
        Assert.Empty(fixture.PaymentProvider.PromptsTo(second.Phone));
    }

    [Fact]
    public async Task ConcurrentCheckouts_NeverOversell()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.AdminAsync(client);
        var sale = await TicketingApi.PublishedEventAsync(client, admin, capacity: 5);

        var customers = new List<IdentityLogin>();
        for (var i = 0; i < 8; i++)
        {
            customers.Add(await _api.CustomerAsync(client));
        }

        var responses = await Task.WhenAll(customers.Select(async c =>
        {
            using var response = await TicketingApi.PlaceOrderAsync(client, c, Items(sale, 1), Guid.NewGuid().ToString("N"));
            return response.StatusCode;
        }));

        Assert.Equal(5, responses.Count(x => x == HttpStatusCode.Created));
        Assert.Equal(3, responses.Count(x => x == HttpStatusCode.Conflict));
        Assert.Equal((5, 5, 0), await _api.TierBucketAsync(sale.TierId));
    }

    [Fact]
    public async Task CancelledPrompt_CanBeRetried_AndOnlyOneOpenOrderPerEvent()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.AdminAsync(client);
        var sale = await TicketingApi.PublishedEventAsync(client, admin);
        var customer = await _api.CustomerAsync(client);

        using var placed = await TicketingApi.PlaceOrderAsync(client, customer, Items(sale, 1), Guid.NewGuid().ToString("N"));
        var orderId = (await TicketingApi.ReadAsync(placed, HttpStatusCode.Created)).GetProperty("id").GetGuid();

        // A second order for the same event while the first waits: refused.
        using (var second = await TicketingApi.PlaceOrderAsync(client, customer, Items(sale, 1), Guid.NewGuid().ToString("N")))
        {
            Assert.Equal("Ticketing.OrderAlreadyOpen", (await TicketingApi.ReadAsync(second, HttpStatusCode.Conflict)).GetProperty("errorKey").GetString());
        }

        // Retry while the prompt is still on the phone: refused.
        var retryPath = $"/api/client/orders/{orderId}/payments";
        using (var early = await IdentityApi.CallAsync(client, HttpMethod.Post, retryPath, new { payerPhone = (string?)null }, customer.AccessToken))
        {
            Assert.Equal("Ticketing.PaymentInProgress", (await TicketingApi.ReadAsync(early, HttpStatusCode.Conflict)).GetProperty("errorKey").GetString());
        }

        var first = Assert.Single(fixture.PaymentProvider.PromptsTo(customer.Phone));
        Assert.Equal(HttpStatusCode.OK, await _api.CompletePromptAsync(client, first, ProviderTransactionStatus.Cancelled));

        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        JsonElement view;
        do
        {
            view = await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/client/orders/{orderId}", customer, null, HttpStatusCode.OK);
            await Task.Delay(200);
        }
        while (view.GetProperty("lastPaymentFailure").ValueKind == JsonValueKind.Null && DateTimeOffset.UtcNow < deadline);

        Assert.Equal("Cancelled", view.GetProperty("lastPaymentFailure").GetString());
        Assert.Equal("AwaitingPayment", view.GetProperty("status").GetString());

        // Retry on another phone, then pay.
        var other = IdentityApi.NewKenyanPhone();
        var retried = await TicketingApi.SendAsync(client, HttpMethod.Post, retryPath, customer, new { payerPhone = "0" + other[4..] }, HttpStatusCode.OK);
        Assert.Equal(2, retried.GetProperty("paymentAttempts").GetInt32());

        var second2 = Assert.Single(fixture.PaymentProvider.PromptsTo(other));
        await _api.CompletePromptAsync(client, second2, ProviderTransactionStatus.Success);
        await TicketingApi.WaitForOrderAsync(client, customer, orderId, "Paid");
    }

    [Fact]
    public async Task ExpiredHold_IsReleased_LatePaymentGetsSeatsBackOrIsFlagged()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.AdminAsync(client);
        var sale = await TicketingApi.PublishedEventAsync(client, admin, capacity: 2);

        // Order A: hold runs out, released; money arrives late while the seats are still free → paid late.
        var a = await _api.CustomerAsync(client);
        using var placedA = await TicketingApi.PlaceOrderAsync(client, a, Items(sale, 1), Guid.NewGuid().ToString("N"));
        var orderA = (await TicketingApi.ReadAsync(placedA, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var promptA = Assert.Single(fixture.PaymentProvider.PromptsTo(a.Phone));

        // The prompt is still pending: within the grace period the job leaves the order alone.
        await _api.ExpireHoldNowAsync(orderA);
        await using (var connection = await _api.OpenTenantAsync())
        await using (var grace = new NpgsqlCommand("""UPDATE ticketing."order" SET "HoldExpiresAt" = now() - interval '1 minute' WHERE "Id" = @id""", connection))
        {
            grace.Parameters.AddWithValue("id", orderA);
            await grace.ExecuteNonQueryAsync();
        }

        await _api.RunTenantJobAsync<ReleaseExpiredHoldsJob>();
        Assert.Equal((2, 1, 0), await _api.TierBucketAsync(sale.TierId));

        await _api.ExpireHoldNowAsync(orderA);
        await _api.RunTenantJobAsync<ReleaseExpiredHoldsJob>();
        Assert.Equal((2, 0, 0), await _api.TierBucketAsync(sale.TierId));
        await TicketingApi.WaitForOrderAsync(client, a, orderA, "Expired");

        await _api.CompletePromptAsync(client, promptA, ProviderTransactionStatus.Success);
        var paidLate = await TicketingApi.WaitForOrderAsync(client, a, orderA, "Paid");
        Assert.Equal(1, paidLate.GetProperty("ticketsIssued").GetInt32());
        Assert.Equal((2, 0, 1), await _api.TierBucketAsync(sale.TierId));

        // Order B: no answer from PalPluss, so the payment times out and the hold is released; C buys the last
        // seat; then PalPluss reports B as paid after all. The money cannot get a seat: Finance must refund it.
        var b = await _api.CustomerAsync(client);
        using var placedB = await TicketingApi.PlaceOrderAsync(client, b, Items(sale, 1), Guid.NewGuid().ToString("N"));
        var orderB = (await TicketingApi.ReadAsync(placedB, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var promptB = Assert.Single(fixture.PaymentProvider.PromptsTo(b.Phone));

        await using (var connection = await _api.OpenTenantAsync())
        await using (var age = new NpgsqlCommand("""UPDATE payments.payment SET "CreatedAt" = now() - interval '10 minutes' WHERE "ReferenceId" = @id""", connection))
        {
            age.Parameters.AddWithValue("id", orderB);
            await age.ExecuteNonQueryAsync();
        }

        await _api.RunTenantJobAsync<CheckPaymentsJob>();
        await _api.ExpireHoldNowAsync(orderB);
        await _api.RunTenantJobAsync<ReleaseExpiredHoldsJob>();
        await TicketingApi.WaitForOrderAsync(client, b, orderB, "Expired");

        var c = await _api.CustomerAsync(client);
        using (var placedC = await TicketingApi.PlaceOrderAsync(client, c, Items(sale, 1), Guid.NewGuid().ToString("N")))
        {
            await TicketingApi.ReadAsync(placedC, HttpStatusCode.Created);
        }

        await _api.CompletePromptAsync(client, promptB, ProviderTransactionStatus.Success);
        var flagged = await TicketingApi.WaitForOrderAsync(client, b, orderB, "NeedsAttention");
        Assert.Equal(0, flagged.GetProperty("ticketsIssued").GetInt32());
        Assert.Equal((2, 1, 1), await _api.TierBucketAsync(sale.TierId));
    }

    [Fact]
    public async Task EarlyAccessPhase_IsForWaitlistedPhonesOnly()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.AdminAsync(client);
        var sale = await TicketingApi.PublishedEventAsync(client, admin, earlyAccess: true);
        var customer = await _api.CustomerAsync(client);

        using (var refused = await TicketingApi.PlaceOrderAsync(client, customer, Items(sale, 1), Guid.NewGuid().ToString("N")))
        {
            Assert.Equal("Ticketing.EarlyAccessOnly", (await TicketingApi.ReadAsync(refused, HttpStatusCode.Forbidden)).GetProperty("errorKey").GetString());
        }

        using (var join = await IdentityApi.CallAsync(client, HttpMethod.Post, "/api/client/waitlist", new
               {
                   eventSlug = sale.Slug, name = "Wanjiru Kamau", phone = customer.Phone, age = 27, gender = "woman",
                   location = "Kilimani", email = (string?)null, privacyAccepted = true, privacyPolicyVersion = "2026-10-01",
                   marketingConsent = false
               }))
        {
            Assert.Equal(HttpStatusCode.Created, join.StatusCode);
        }

        using var allowed = await TicketingApi.PlaceOrderAsync(client, customer, Items(sale, 1), Guid.NewGuid().ToString("N"));
        await TicketingApi.ReadAsync(allowed, HttpStatusCode.Created);
    }

    [Fact]
    public async Task Checkout_RequiresSignInAndIdempotencyKey_AndChecksTheBasket()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.AdminAsync(client);
        var sale = await TicketingApi.PublishedEventAsync(client, admin, maxTicketsPerOrder: 4);
        var customer = await _api.CustomerAsync(client);

        using (var anonymous = await IdentityApi.CallAsync(client, HttpMethod.Post, "/api/client/orders", Items(sale, 1)))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        using (var noKey = await TicketingApi.PlaceOrderAsync(client, customer, Items(sale, 1), null))
        {
            Assert.Equal("Ticketing.IdempotencyKeyRequired", (await TicketingApi.ReadAsync(noKey, HttpStatusCode.BadRequest)).GetProperty("errorKey").GetString());
        }

        using (var tooMany = await TicketingApi.PlaceOrderAsync(client, customer, Items(sale, 5), Guid.NewGuid().ToString("N")))
        {
            Assert.Equal("Ticketing.TooManyTickets", (await TicketingApi.ReadAsync(tooMany, HttpStatusCode.BadRequest)).GetProperty("errorKey").GetString());
        }

        using (var unknown = await TicketingApi.PlaceOrderAsync(client, customer, new { eventSlug = sale.Slug, items = new[] { new { tierId = Guid.NewGuid(), quantity = 1 } } }, Guid.NewGuid().ToString("N")))
        {
            var problem = await TicketingApi.ReadAsync(unknown, HttpStatusCode.BadRequest);
            Assert.StartsWith("Items[0]:", problem.GetProperty("errors")[0].GetString(), StringComparison.Ordinal);
        }

        // Another customer cannot read the order.
        using var placed = await TicketingApi.PlaceOrderAsync(client, customer, Items(sale, 1), Guid.NewGuid().ToString("N"));
        var orderId = (await TicketingApi.ReadAsync(placed, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var stranger = await _api.CustomerAsync(client);
        await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/client/orders/{orderId}", stranger, null, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ProviderRefusal_IsReported_AndCallbacksCannotForgeSuccess()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.AdminAsync(client);
        var sale = await TicketingApi.PublishedEventAsync(client, admin);
        var customer = await _api.CustomerAsync(client);

        fixture.PaymentProvider.RefuseNext = "RATE_LIMIT_EXCEEDED";
        using var placed = await TicketingApi.PlaceOrderAsync(client, customer, Items(sale, 1), Guid.NewGuid().ToString("N"));
        var order = await TicketingApi.ReadAsync(placed, HttpStatusCode.Created);
        var payment = order.GetProperty("payment");
        Assert.Equal("Failed", payment.GetProperty("status").GetString());
        Assert.Equal("Payments.ProviderBusy", payment.GetProperty("errorKey").GetString());
        Assert.Equal(30, payment.GetProperty("retryAfterSeconds").GetInt32());
        Assert.Equal("Payments.ProviderBusy", order.GetProperty("lastPaymentFailure").GetString());

        // Retry works now; then a forged "success" callback while PalPluss still says PENDING changes nothing.
        var orderId = order.GetProperty("id").GetGuid();
        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/client/orders/{orderId}/payments", customer, new { payerPhone = (string?)null }, HttpStatusCode.OK);
        var prompt = Assert.Single(fixture.PaymentProvider.PromptsTo(customer.Phone));

        using (var forged = await IdentityApi.CallAsync(client, HttpMethod.Post, prompt.CallbackPath, new
               {
                   event_type = "transaction.success",
                   transaction = new { id = prompt.TransactionId, status = "SUCCESS", mpesa_receipt = "FAKE" }
               }))
        {
            Assert.Equal(HttpStatusCode.OK, forged.StatusCode);
        }

        await Task.Delay(1000);
        var still = await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/client/orders/{orderId}", customer, null, HttpStatusCode.OK);
        Assert.Equal("AwaitingPayment", still.GetProperty("status").GetString());

        // Unknown callback token: 404.
        using var unknown = await IdentityApi.CallAsync(client, HttpMethod.Post, "/webhooks/palpluss/not-a-real-token", new { });
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
    }

    [Fact]
    public async Task WrongAmount_IsHeldForFinanceReview_NotFulfilled()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.AdminAsync(client);
        var sale = await TicketingApi.PublishedEventAsync(client, admin, price: 1000m);
        var customer = await _api.CustomerAsync(client);

        using var placed = await TicketingApi.PlaceOrderAsync(client, customer, Items(sale, 1), Guid.NewGuid().ToString("N"));
        var orderId = (await TicketingApi.ReadAsync(placed, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var prompt = Assert.Single(fixture.PaymentProvider.PromptsTo(customer.Phone));

        await _api.CompletePromptAsync(client, prompt, ProviderTransactionStatus.Success, reportedAmount: 10m);
        await Task.Delay(1000);

        var view = await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/client/orders/{orderId}", customer, null, HttpStatusCode.OK);
        Assert.Equal("AwaitingPayment", view.GetProperty("status").GetString());
        Assert.Equal("NeedsReview", view.GetProperty("payment").GetProperty("status").GetString());

        // Finance sees it; a customer without a staff role does not.
        var finance = await _api.AdminAsync(client, IdentityRole.Finance);
        var list = await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/admin/payments?status=NeedsReview&referenceId={orderId}", finance, null, HttpStatusCode.OK);
        var row = Assert.Single(list.EnumerateArray());
        Assert.Equal(10m, row.GetProperty("reportedAmount").GetDecimal());
        Assert.DoesNotContain(customer.Phone[4..], list.ToString(), StringComparison.Ordinal);

        await TicketingApi.SendAsync(client, HttpMethod.Get, "/api/admin/payments", customer, null, HttpStatusCode.Forbidden);

        // Sales counters for the admin.
        var sales = await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/admin/events/{sale.EventId}/sales", admin, null, HttpStatusCode.OK);
        var tier = sales.GetProperty("buckets").EnumerateArray().Single(x => x.GetProperty("refId").GetGuid() == sale.TierId);
        Assert.Equal(1, tier.GetProperty("held").GetInt32());
    }

    [Fact]
    public async Task StatusJob_PicksUpMissedCallbacks()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.AdminAsync(client);
        var sale = await TicketingApi.PublishedEventAsync(client, admin);
        var customer = await _api.CustomerAsync(client);

        using var placed = await TicketingApi.PlaceOrderAsync(client, customer, Items(sale, 1), Guid.NewGuid().ToString("N"));
        var orderId = (await TicketingApi.ReadAsync(placed, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var prompt = Assert.Single(fixture.PaymentProvider.PromptsTo(customer.Phone));

        // PalPluss completes it but the callback never arrives; age the payment past the first-check delay.
        fixture.PaymentProvider.Complete(prompt.TransactionId, ProviderTransactionStatus.Success);
        await using (var connection = await _api.OpenTenantAsync())
        await using (var age = new NpgsqlCommand("""UPDATE payments.payment SET "CreatedAt" = now() - interval '1 minute' WHERE "ReferenceId" = @id""", connection))
        {
            age.Parameters.AddWithValue("id", orderId);
            await age.ExecuteNonQueryAsync();
        }

        await _api.RunTenantJobAsync<CheckPaymentsJob>();
        await TicketingApi.WaitForOrderAsync(client, customer, orderId, "Paid");
    }

    private static object Items(TicketingApi.SaleEvent sale, int quantity) =>
        new { eventSlug = sale.Slug, items = new[] { new { tierId = sale.TierId, quantity } } };
}
