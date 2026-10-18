using System.Net;
using System.Text.Json;
using Api.IntegrationTests.Identity.Auditing;
using Api.IntegrationTests.Ticketing;
using Common.Application.Notifications;
using Communications.Application.Alerts;
using Communications.Application.Broadcasts;
using Communications.Domain.Common;
using Identity.Domain.Authorization;
using Identity.Domain.Roles;
using Npgsql;
using Xunit;

namespace Api.IntegrationTests.Communications;

/// <summary>
///     UF-020 end to end: waitlist marketing broadcasts skip ticket buyers, numbers without consent and opted-out
///     numbers; ticket-holder information goes to every holder; the throttled dispatcher sends; the message log and
///     delivery reports show what arrived; only Tenant Admins send, Support only reads.
/// </summary>
[Collection(IdentityApiCollection.Name)]
public sealed class CommunicationsHttpTests(IdentityAuditApiFixture fixture)
{
    private const string Broadcasts = "/api/admin/broadcasts";
    private const string WebhookToken = "integration-test-delivery-report-token";

    private readonly TicketingApi _api = new(fixture);

    [Fact]
    public async Task BroadcastsReachTheRightPeopleAndDeliveryIsTracked()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.AdminAsync(client);
        var support = await _api.AdminAsync(client, IdentityRole.Support);
        var sale = await TicketingApi.PublishedEventAsync(client, admin);

        // Four on the waitlist: A (will buy), B (will opt out), C (plain), D (no marketing consent).
        var (a, b, c, d) = (IdentityApi.NewKenyanPhone(), IdentityApi.NewKenyanPhone(), IdentityApi.NewKenyanPhone(), IdentityApi.NewKenyanPhone());
        await JoinAsync(client, sale.Slug, a, "Achieng Otieno", consent: true);
        await JoinAsync(client, sale.Slug, b, "Baraka Mwangi", consent: true);
        await JoinAsync(client, sale.Slug, c, "Chebet Kiprop", consent: true);
        await JoinAsync(client, sale.Slug, d, "Dalmas Ouma", consent: false);

        var buyer = await _api.CustomerAsync(client, a);
        await _api.BuyAsync(client, buyer, sale, 1);

        var optingOut = await _api.CustomerAsync(client, b);
        var prefs = await TicketingApi.SendAsync(client, HttpMethod.Get, "/api/client/communications/preferences", optingOut, null, HttpStatusCode.OK);
        Assert.True(prefs.GetProperty("marketing").GetBoolean());
        await TicketingApi.SendAsync(client, HttpMethod.Put, "/api/client/communications/preferences", optingOut, new { marketing = false }, HttpStatusCode.OK);

        // Sales-open broadcast to waitlist members without a ticket.
        var created = await TicketingApi.SendAsync(client, HttpMethod.Post, Broadcasts, admin,
            new { name = "Sales open", eventId = sale.EventId, audience = "WaitlistWithoutTicket", body = "Hi {firstName}, tickets are on sale now!" },
            HttpStatusCode.Created);
        var broadcastId = created.GetProperty("id").GetGuid();
        Assert.Equal("Draft", created.GetProperty("status").GetString());
        Assert.True(created.GetProperty("marketing").GetBoolean());

        var preview = await TicketingApi.SendAsync(client, HttpMethod.Get, $"{Broadcasts}/{broadcastId}/preview", admin, null, HttpStatusCode.OK);
        Assert.Equal(2, preview.GetProperty("total").GetInt32());
        Assert.Equal(1, preview.GetProperty("optedOut").GetInt32());
        Assert.Equal(1, preview.GetProperty("willReceive").GetInt32());
        Assert.EndsWith("STOP*456#", preview.GetProperty("sampleText").GetString(), StringComparison.Ordinal);

        // Support reads, does not send; customers see nothing.
        await TicketingApi.SendAsync(client, HttpMethod.Get, Broadcasts, support, null, HttpStatusCode.OK);
        await TicketingApi.SendAsync(client, HttpMethod.Post, $"{Broadcasts}/{broadcastId}/schedule", support, new { sendAt = (DateTimeOffset?)null }, HttpStatusCode.Forbidden);
        await TicketingApi.SendAsync(client, HttpMethod.Get, "/api/admin/messages", buyer, null, HttpStatusCode.Forbidden);

        var scheduled = await TicketingApi.SendAsync(client, HttpMethod.Post, $"{Broadcasts}/{broadcastId}/schedule", admin, new { sendAt = (DateTimeOffset?)null }, HttpStatusCode.OK);
        Assert.Equal("Scheduled", scheduled.GetProperty("status").GetString());
        await TicketingApi.SendAsync(client, HttpMethod.Put, $"{Broadcasts}/{broadcastId}", admin,
            new { name = "Changed", eventId = sale.EventId, audience = "WaitlistMarketing", body = "x" }, HttpStatusCode.Conflict);

        await _api.RunTenantJobAsync<BroadcastDispatchJob>();

        // (Everyone also got the waitlist confirmation SMS when they joined.)
        static IEnumerable<string> OnSale(IEnumerable<SmsMessage> sent) =>
            sent.Select(x => x.Message).Where(x => x.Contains("on sale", StringComparison.Ordinal));

        Assert.Equal("Hi Chebet, tickets are on sale now! STOP*456#", Assert.Single(OnSale(fixture.SmsGateway.SentTo(c))));
        Assert.Empty(OnSale(fixture.SmsGateway.SentTo(b)));
        Assert.Empty(OnSale(fixture.SmsGateway.SentTo(d)));
        Assert.DoesNotContain(fixture.SmsGateway.SentTo(a), x => x.Message.Contains("on sale", StringComparison.Ordinal));

        var done = await TicketingApi.SendAsync(client, HttpMethod.Get, $"{Broadcasts}/{broadcastId}", support, null, HttpStatusCode.OK);
        Assert.Equal("Completed", done.GetProperty("status").GetString());
        Assert.Equal((1, 1, 1, 0), (done.GetProperty("recipients").GetInt32(), done.GetProperty("skipped").GetInt32(), done.GetProperty("sent").GetInt32(), done.GetProperty("failed").GetInt32()));

        // The log shows the message; the provider's delivery report marks it delivered (twice is fine).
        var log = await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/admin/messages?broadcastId={broadcastId}", support, null, HttpStatusCode.OK);
        var message = Assert.Single(log.EnumerateArray());
        Assert.Equal(c, message.GetProperty("recipient").GetString());
        Assert.Equal("Sent", message.GetProperty("status").GetString());
        var providerId = await ProviderIdAsync(message.GetProperty("id").GetGuid());

        await WebhookAsync(client, "wrong-token-that-is-long-enough", new { messageId = providerId, status = "DeliveredToTerminal" }, HttpStatusCode.NotFound);
        await WebhookAsync(client, WebhookToken, new { messageId = providerId, status = "DeliveredToTerminal" }, HttpStatusCode.OK);
        await WebhookAsync(client, WebhookToken, new[] { new { messageId = providerId, status = "Failed" }, new { messageId = "unknown", status = "Delivered" } }, HttpStatusCode.OK);

        log = await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/admin/messages?broadcastId={broadcastId}", support, null, HttpStatusCode.OK);
        Assert.Equal("Delivered", Assert.Single(log.EnumerateArray()).GetProperty("status").GetString());

        // Searching by the number as typed (07...) finds it.
        var byPhone = await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/admin/messages?recipient=0{c[4..]}&kind=Broadcast", support, null, HttpStatusCode.OK);
        Assert.Single(byPhone.EnumerateArray());

        // Event information goes to ticket holders, opted out or not.
        var info = await TicketingApi.SendAsync(client, HttpMethod.Post, Broadcasts, admin,
            new { name = "Gates", eventId = sale.EventId, audience = "TicketHolders", body = "Gates open at 6pm, {firstName}." }, HttpStatusCode.Created);
        await TicketingApi.SendAsync(client, HttpMethod.Post, $"{Broadcasts}/{info.GetProperty("id").GetGuid()}/schedule", admin, new { sendAt = (DateTimeOffset?)null }, HttpStatusCode.OK);
        await _api.RunTenantJobAsync<BroadcastDispatchJob>();
        Assert.Contains(fixture.SmsGateway.SentTo(a), x => x.Message.StartsWith("Gates open at 6pm", StringComparison.Ordinal) && !x.Message.Contains("STOP", StringComparison.Ordinal));

        // A broadcast scheduled for later can be cancelled and never sends.
        var later = await TicketingApi.SendAsync(client, HttpMethod.Post, Broadcasts, admin,
            new { name = "Later", eventId = sale.EventId, audience = "WaitlistMarketing", body = "Soon." }, HttpStatusCode.Created);
        var laterId = later.GetProperty("id").GetGuid();
        await TicketingApi.SendAsync(client, HttpMethod.Post, $"{Broadcasts}/{laterId}/schedule", admin, new { sendAt = DateTimeOffset.UtcNow.AddDays(2) }, HttpStatusCode.OK);
        await TicketingApi.SendAsync(client, HttpMethod.Post, $"{Broadcasts}/{laterId}/cancel", admin, null, HttpStatusCode.OK);
        await _api.RunTenantJobAsync<BroadcastDispatchJob>();
        Assert.DoesNotContain(fixture.SmsGateway.SentTo(c), x => x.Message.StartsWith("Soon", StringComparison.Ordinal));

        // Staff can see and undo an opt-out; the audit log has the broadcast's story without phone numbers.
        var optOuts = await TicketingApi.SendAsync(client, HttpMethod.Get, "/api/admin/opt-outs", admin, null, HttpStatusCode.OK);
        Assert.Contains(optOuts.EnumerateArray(), x => x.GetProperty("phone").GetString() == b);
        await TicketingApi.SendAsync(client, HttpMethod.Delete, $"/api/admin/opt-outs/{Uri.EscapeDataString(b)}", admin, null, HttpStatusCode.NoContent);
        prefs = await TicketingApi.SendAsync(client, HttpMethod.Get, "/api/client/communications/preferences", optingOut, null, HttpStatusCode.OK);
        Assert.True(prefs.GetProperty("marketing").GetBoolean());

        var actions = await AuditActionsAsync(broadcastId);
        Assert.Equal(
            ["communications.broadcast.completed", "communications.broadcast.created", "communications.broadcast.scheduled", "communications.broadcast.started"],
            actions);
    }

    [Fact]
    public async Task TemplatesAreSavedEditedAndArchived()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.AdminAsync(client);

        var created = await TicketingApi.SendAsync(client, HttpMethod.Post, "/api/admin/message-templates", admin,
            new { name = "Gates open", body = "Gates open at {time}." }, HttpStatusCode.Created);
        var id = created.GetProperty("id").GetGuid();

        await TicketingApi.SendAsync(client, HttpMethod.Put, $"/api/admin/message-templates/{id}", admin,
            new { name = "Gates open", body = "" }, HttpStatusCode.BadRequest);
        await TicketingApi.SendAsync(client, HttpMethod.Put, $"/api/admin/message-templates/{id}", admin,
            new { name = "Gates open", body = "Gates open at 6pm." }, HttpStatusCode.OK);

        var list = await TicketingApi.SendAsync(client, HttpMethod.Get, "/api/admin/message-templates", admin, null, HttpStatusCode.OK);
        Assert.Contains(list.EnumerateArray(), x => x.GetProperty("id").GetGuid() == id && x.GetProperty("body").GetString() == "Gates open at 6pm.");

        await TicketingApi.SendAsync(client, HttpMethod.Delete, $"/api/admin/message-templates/{id}", admin, null, HttpStatusCode.NoContent);
        list = await TicketingApi.SendAsync(client, HttpMethod.Get, "/api/admin/message-templates", admin, null, HttpStatusCode.OK);
        Assert.DoesNotContain(list.EnumerateArray(), x => x.GetProperty("id").GetGuid() == id);
    }

    [Fact]
    public async Task OperatorChecksRunAgainstEveryModuleWithoutErrors()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        // Every IOperationalSignal (outbox, payments, holds, SMS) runs its SQL against a real tenant database.
        await _api.RunTenantJobAsync<OperatorAlertsJob>();

        Assert.DoesNotContain(fixture.SmsGateway.SentTo("+254700000099"), x => x.Message.Contains("could not run", StringComparison.Ordinal));
    }

    [Fact]
    public void CommunicationsPermissionsAreInIdentitysCatalog() =>
        Assert.Subset(IdentityPermission.All.ToHashSet(StringComparer.Ordinal), CommunicationsPermission.All.ToHashSet(StringComparer.Ordinal));

    private static async Task JoinAsync(HttpClient client, string slug, string phone, string name, bool consent)
    {
        using var response = await IdentityApi.CallAsync(client, HttpMethod.Post, "/api/client/waitlist", new
        {
            eventSlug = slug,
            name,
            phone,
            age = 27,
            gender = "woman",
            location = "Kilimani",
            email = (string?)null,
            privacyAccepted = true,
            privacyPolicyVersion = "2026-10-01",
            marketingConsent = consent
        });

        Assert.True(response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private static async Task WebhookAsync(HttpClient client, string token, object body, HttpStatusCode expected)
    {
        using var response = await IdentityApi.CallAsync(client, HttpMethod.Post, $"/webhooks/sms/{token}", body);
        Assert.Equal(expected, response.StatusCode);
    }

    private async Task<string> ProviderIdAsync(Guid messageId)
    {
        await using var connection = await _api.OpenTenantAsync();
        await using var command = new NpgsqlCommand("""SELECT "ProviderMessageId" FROM communications.message WHERE "Id" = @id""", connection);
        command.Parameters.AddWithValue("id", messageId);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private async Task<List<string>> AuditActionsAsync(Guid broadcastId)
    {
        await using var connection = await _api.OpenTenantAsync();
        await using var command = new NpgsqlCommand(
            """SELECT "Action", "Metadata"::text FROM communications.audit_log WHERE "TargetId" = @target ORDER BY "Action" """,
            connection);
        command.Parameters.AddWithValue("target", broadcastId.ToString("D"));

        var actions = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            actions.Add(reader.GetString(0));
            Assert.DoesNotContain("+254", reader.GetString(1), StringComparison.Ordinal);
        }

        return actions;
    }
}
