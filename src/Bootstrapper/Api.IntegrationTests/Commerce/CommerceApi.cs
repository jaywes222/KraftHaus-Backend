using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Api.IntegrationTests.Identity.Auditing;
using Api.IntegrationTests.Ticketing;
using Common.Domain.StronglyTypedIds;
using Identity.Domain.Authorization;
using Identity.Domain.Common;
using Identity.Domain.Roles;
using Identity.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Payments.Domain.Payments;
using Xunit;

namespace Api.IntegrationTests.Commerce;

/// <summary>Kraft, till and menu helpers shared by the Commerce HTTP tests.</summary>
internal sealed class CommerceApi(IdentityAuditApiFixture fixture)
{
    public TicketingApi Ticketing { get; } = new(fixture);

    /// <summary>Signs a new staff member in with a Kraft-scoped role (Kraft Owner or Kraft Staff).</summary>
    public async Task<IdentityLogin> KraftStaffAsync(HttpClient client, Guid kraftId, IdentityRole role = IdentityRole.KraftOwner)
    {
        var login = await Ticketing.CustomerAsync(client);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        db.RoleAssignments.Add(RoleAssignment.Grant(
            new RoleAssignmentId(Guid.CreateVersion7()),
            new ApplicationUserId(login.UserId),
            role,
            RoleScopeType.Kraft,
            kraftId,
            DateTimeOffset.UtcNow).Value!);

        await db.SaveChangesAsync();

        return login;
    }

    public static async Task<Guid> CreateKraftAsync(HttpClient client, IdentityLogin admin, string name, Guid eventId, decimal commission = 10m)
    {
        var kraft = await TicketingApi.SendAsync(client, HttpMethod.Post, "/api/admin/krafts", admin,
            new { name, discipline = "Food", description = "Slow-cooked Kenyan street food." }, HttpStatusCode.Created);
        var kraftId = kraft.GetProperty("id").GetGuid();

        await TicketingApi.SendAsync(client, HttpMethod.Put, $"/api/admin/krafts/{kraftId}/events/{eventId}", admin,
            new { commissionPercent = commission, stall = "Courtyard 4" }, HttpStatusCode.OK);

        return kraftId;
    }

    /// <summary>Submits, tests (the test prompt succeeds) and approves a till for the Kraft at the event.</summary>
    public async Task<(Guid TillId, Guid ChannelId)> ApprovedTillAsync(
        HttpClient client,
        IdentityLogin admin,
        Guid kraftId,
        Guid eventId,
        IdentityLogin finance)
    {
        var till = await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/krafts/{kraftId}/tills", admin,
            new { eventId, name = "Stall till", shortcodeType = "BuyGoods", shortcode = "543210", accountNumber = (string?)null },
            HttpStatusCode.Created);
        var tillId = till.GetProperty("id").GetGuid();
        var channelId = Guid.NewGuid();

        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/tills/{tillId}/test", finance,
            new { channelId, phone = finance.Phone }, HttpStatusCode.OK);

        var prompt = fixture.PaymentProvider.PromptsTo(finance.Phone)[^1];
        await Ticketing.CompletePromptAsync(client, prompt, ProviderTransactionStatus.Success);
        await WaitForTillAsync(client, finance, tillId, "Passed");

        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/tills/{tillId}/approve", finance, null, HttpStatusCode.OK);

        return (tillId, channelId);
    }

    /// <summary>A Kraft at the event with an approved till.</summary>
    public async Task<Guid> SellingKraftAsync(HttpClient client, IdentityLogin admin, IdentityLogin finance, string name, Guid eventId, decimal commission = 10m)
    {
        var kraftId = await CreateKraftAsync(client, admin, name, eventId, commission);
        await ApprovedTillAsync(client, admin, kraftId, eventId, finance);
        return kraftId;
    }

    public static async Task<Guid> MenuItemAsync(
        HttpClient client,
        IdentityLogin owner,
        Guid kraftId,
        Guid eventId,
        string name,
        decimal price,
        string category = "Food",
        int? liveStock = null,
        int? reservationAllocation = null)
    {
        var item = await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/krafts/{kraftId}/menu", owner,
            new { eventId, name, description = (string?)null, category, price, liveStock, reservationAllocation, ageRestricted = (bool?)null, sortOrder = (int?)null },
            HttpStatusCode.Created);
        return item.GetProperty("id").GetGuid();
    }

    public static async Task<HttpResponseMessage> CheckoutAsync(HttpClient client, IdentityLogin customer, object body, string? idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/client/kraft-checkouts")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", customer.AccessToken);
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return await client.SendAsync(request);
    }

    public static async Task<JsonElement> WaitForKraftOrderAsync(HttpClient client, IdentityLogin customer, Guid orderId, string status)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        JsonElement last = default;

        while (DateTimeOffset.UtcNow < deadline)
        {
            last = await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/client/kraft-orders/{orderId}", customer, null, HttpStatusCode.OK);
            if (last.GetProperty("status").GetString() == status)
            {
                return last;
            }

            await Task.Delay(200);
        }

        Assert.Fail($"Kraft order {orderId} never reached {status}; last: {last}");
        return last;
    }

    /// <summary>Pays one order of a checkout end to end (prompt → PalPluss success → outbox).</summary>
    public async Task<JsonElement> PayAsync(HttpClient client, IdentityLogin customer, Guid orderId)
    {
        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/client/kraft-orders/{orderId}/pay", customer,
            new { payerPhone = (string?)null }, HttpStatusCode.OK);
        await Ticketing.CompletePromptAsync(client, fixture.PaymentProvider.PromptsTo(customer.Phone)[^1], ProviderTransactionStatus.Success);
        return await WaitForKraftOrderAsync(client, customer, orderId, "Paid");
    }

    public static async Task<JsonElement> WaitForTillAsync(HttpClient client, IdentityLogin finance, Guid tillId, string testStatus)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
        JsonElement last = default;

        while (DateTimeOffset.UtcNow < deadline)
        {
            last = await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/admin/tills/{tillId}", finance, null, HttpStatusCode.OK);
            if (last.GetProperty("testStatus").GetString() == testStatus)
            {
                return last;
            }

            await Task.Delay(200);
        }

        Assert.Fail($"Till {tillId} never reached test status {testStatus}; last: {last}");
        return last;
    }
}
