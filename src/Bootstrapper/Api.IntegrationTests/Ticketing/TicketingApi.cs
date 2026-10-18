using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Api.IntegrationTests.Identity.Auditing;
using Common.Application.Messaging;
using Common.Application.Tenancy;
using Common.Domain.StronglyTypedIds;
using Identity.Domain.Authorization;
using Identity.Domain.Common;
using Identity.Domain.Roles;
using Identity.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Payments.Domain.Payments;
using Xunit;

namespace Api.IntegrationTests.Ticketing;

/// <summary>Event setup, sign-in and polling helpers shared by the Ticketing HTTP tests.</summary>
internal sealed class TicketingApi(IdentityAuditApiFixture fixture)
{
    public const string Admin = "/api/admin";

    public sealed record SaleEvent(Guid EventId, string Slug, Guid DateId, Guid TierId, Guid PhaseId, Guid? CoupleOfferId);

    public async Task<IdentityLogin> AdminAsync(HttpClient client, IdentityRole role = IdentityRole.TenantAdmin)
    {
        var login = await IdentityApi.SignInAsync(client, fixture.OtpSender);
        await GrantAsync(login.UserId, role);
        return login;
    }

    public Task<IdentityLogin> CustomerAsync(HttpClient client, string? phone = null) =>
        IdentityApi.SignInAsync(client, fixture.OtpSender, phone);

    public async Task GrantAsync(Guid userId, IdentityRole role)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        db.RoleAssignments.Add(RoleAssignment.Grant(
            new RoleAssignmentId(Guid.CreateVersion7()),
            new ApplicationUserId(userId),
            role,
            RoleScopeType.Tenant,
            fixture.TenantId,
            DateTimeOffset.UtcNow).Value!);

        await db.SaveChangesAsync();
    }

    /// <summary>A published event: one date in 20 days, one tier, one phase on sale now, optionally a Couple offer.</summary>
    public static async Task<SaleEvent> PublishedEventAsync(
        HttpClient client,
        IdentityLogin admin,
        int capacity = 100,
        decimal price = 1000m,
        bool earlyAccess = false,
        int? phaseAllocation = null,
        bool withCouple = false,
        int holdMinutes = 10,
        int maxTicketsPerOrder = 10,
        bool lipaMdogoMdogo = true,
        TimeSpan? startsIn = null)
    {
        var now = DateTimeOffset.UtcNow;
        var slug = $"tk-{Guid.NewGuid():N}"[..20];

        var created = await SendAsync(client, HttpMethod.Post, $"{Admin}/events", admin, new
        {
            slug,
            title = "KraftHaus 001",
            summary = "Food, drinks and music.",
            posterUrl = "https://cdn.example.com/p.jpg"
        }, HttpStatusCode.Created);
        var eventId = created.GetProperty("id").GetGuid();

        await SendAsync(client, HttpMethod.Put, $"{Admin}/events/{eventId}/venue/inline", admin,
            new { name = "KICC Grounds", city = "Nairobi" }, HttpStatusCode.OK);

        var startsAt = now + (startsIn ?? TimeSpan.FromDays(20));
        var date = await SendAsync(client, HttpMethod.Post, $"{Admin}/events/{eventId}/dates", admin,
            new { label = "Day 1", startsAt, endsAt = startsAt.AddHours(8), doorsOpenAt = startsAt.AddHours(-1) },
            HttpStatusCode.Created);
        var dateId = date.GetProperty("id").GetGuid();

        var tier = await SendAsync(client, HttpMethod.Post, $"{Admin}/events/{eventId}/dates/{dateId}/tiers", admin,
            new { name = "Regular", capacity }, HttpStatusCode.Created);
        var tierId = tier.GetProperty("id").GetGuid();

        var phase = await SendAsync(client, HttpMethod.Post, $"{Admin}/events/{eventId}/dates/{dateId}/tiers/{tierId}/phases", admin,
            new { name = earlyAccess ? "Early access" : "Early Bird", price, startsAt = now.AddHours(-1), endsAt = Min(now.AddDays(7), startsAt), allocation = phaseAllocation, earlyAccess },
            HttpStatusCode.Created);
        var phaseId = phase.GetProperty("id").GetGuid();

        Guid? couple = null;
        if (withCouple)
        {
            var offer = await SendAsync(client, HttpMethod.Post, $"{Admin}/events/{eventId}/dates/{dateId}/tiers/{tierId}/offers", admin,
                new { kind = "Couple", name = "Couple ticket", admits = 2, price = price * 2 - 200, startsAt = now.AddHours(-1), endsAt = Min(now.AddDays(7), startsAt) },
                HttpStatusCode.Created);
            couple = offer.GetProperty("id").GetGuid();
        }

        await SendAsync(client, HttpMethod.Put, $"{Admin}/events/{eventId}/policies", admin, new
        {
            refundsEnabled = false,
            transfersEnabled = true,
            maxTransfersPerTicket = 1,
            lipaMdogoMdogoEnabled = lipaMdogoMdogo,
            holdMinutes,
            maxTicketsPerOrder
        }, HttpStatusCode.OK);

        await SendAsync(client, HttpMethod.Post, $"{Admin}/events/{eventId}/publish", admin, null, HttpStatusCode.OK);

        return new SaleEvent(eventId, slug, dateId, tierId, phaseId, couple);
    }

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;

    public static async Task<HttpResponseMessage> PlaceOrderAsync(
        HttpClient client,
        IdentityLogin customer,
        object body,
        string? idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/client/orders") { Content = JsonContent.Create(body) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customer.AccessToken);
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return await client.SendAsync(request);
    }

    public static async Task<JsonElement> ReadAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        var raw = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Expected {(int)expected}, got {(int)response.StatusCode}: {raw}");
        return raw.Length == 0 ? default : JsonDocument.Parse(raw).RootElement.Clone();
    }

    public static async Task<JsonElement> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        IdentityLogin caller,
        object? body,
        HttpStatusCode expected)
    {
        using var response = await IdentityApi.CallAsync(client, method, path, body, caller.AccessToken);
        return await ReadAsync(response, expected);
    }

    /// <summary>Plays PalPluss: scripts the outcome, then calls the payment's callback URL.</summary>
    public async Task<HttpStatusCode> CompletePromptAsync(
        HttpClient client,
        ScriptedPaymentProvider.Prompt prompt,
        ProviderTransactionStatus status,
        decimal? reportedAmount = null,
        string? callbackReceipt = null)
    {
        fixture.PaymentProvider.Complete(prompt.TransactionId, status, reportedAmount);

        using var response = await IdentityApi.CallAsync(client, HttpMethod.Post, prompt.CallbackPath, new
        {
            @event = "transaction.updated",
            event_type = "transaction.success",
            transaction = new { id = prompt.TransactionId, status = "SUCCESS", mpesa_receipt = callbackReceipt }
        });

        return response.StatusCode;
    }

    /// <summary>Polls the customer's order until <paramref name="status" /> (the outbox delivers asynchronously).</summary>
    public static async Task<JsonElement> WaitForOrderAsync(HttpClient client, IdentityLogin customer, Guid orderId, string status)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        JsonElement last = default;

        while (DateTimeOffset.UtcNow < deadline)
        {
            last = await SendAsync(client, HttpMethod.Get, $"/api/client/orders/{orderId}", customer, null, HttpStatusCode.OK);
            if (last.GetProperty("status").GetString() == status)
            {
                return last;
            }

            await Task.Delay(200);
        }

        Assert.Fail($"Order {orderId} never reached {status}; last: {last}");
        return last;
    }

    /// <summary>Buys <paramref name="quantity" /> admissions end to end and returns the customer's tickets for the event.</summary>
    public async Task<IReadOnlyList<JsonElement>> BuyAsync(HttpClient client, IdentityLogin customer, SaleEvent sale, int quantity)
    {
        using var placed = await PlaceOrderAsync(
            client,
            customer,
            new { eventSlug = sale.Slug, items = new[] { new { tierId = sale.TierId, quantity } } },
            Guid.NewGuid().ToString("N"));
        var orderId = (await ReadAsync(placed, HttpStatusCode.Created)).GetProperty("id").GetGuid();

        var prompt = fixture.PaymentProvider.PromptsTo(customer.Phone)[^1];
        await CompletePromptAsync(client, prompt, ProviderTransactionStatus.Success);
        await WaitForOrderAsync(client, customer, orderId, "Paid");

        var tickets = await SendAsync(client, HttpMethod.Get, "/api/client/tickets", customer, null, HttpStatusCode.OK);
        return [.. tickets.EnumerateArray().Where(x => x.GetProperty("eventId").GetGuid() == sale.EventId)];
    }

    public async Task RunTenantJobAsync<TJob>()
        where TJob : ITenantJob
    {
        var runner = fixture.Factory.Services.GetRequiredService<ITenantScopeRunner>();
        await runner.RunInTenantAsync(fixture.TenantId, (scope, ct) => scope.GetRequiredService<TJob>().ExecuteAsync(ct), CancellationToken.None);
    }

    public async Task<NpgsqlConnection> OpenTenantAsync()
    {
        var connection = new NpgsqlConnection(fixture.TenantDatabaseConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    public async Task<(int Total, int Held, int Sold)> TierBucketAsync(Guid tierId)
    {
        await using var connection = await OpenTenantAsync();
        await using var command = new NpgsqlCommand(
            """SELECT "Total", "Held", "Sold" FROM ticketing.capacity_bucket WHERE "Kind" = 'Tier' AND "RefId" = @id""",
            connection);
        command.Parameters.AddWithValue("id", tierId);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2));
    }

    public async Task ExpireHoldNowAsync(Guid orderId)
    {
        await using var connection = await OpenTenantAsync();
        await using var command = new NpgsqlCommand(
            """UPDATE ticketing."order" SET "HoldExpiresAt" = now() - interval '1 hour' WHERE "Id" = @id""",
            connection);
        command.Parameters.AddWithValue("id", orderId);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
    }
}
