using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.IntegrationTests.Identity.Auditing;
using Common.Domain.StronglyTypedIds;
using Identity.Domain.Authorization;
using Identity.Domain.Common;
using Identity.Domain.Roles;
using Identity.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Api.IntegrationTests.Ticketing;

/// <summary>
///     UF-001 through the real host and a real tenant database: the public waitlist contract the Coming Soon page
///     calls (Client docs/api/WAITLIST-CONTRACT.md), the one-phone-per-event rule, the confirmation SMS, tenant
///     isolation and the audit trail.
/// </summary>
[Collection(IdentityApiCollection.Name)]
public sealed class WaitlistHttpTests(IdentityAuditApiFixture fixture)
{
    private const string Admin = "/api/admin";
    private const string Waitlist = "/api/client/waitlist";

    [Fact]
    public async Task Join_Returns201_SavesTheNormalisedEntry_SendsOneSms_AndAuditsWithoutPersonalData()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var slug = await PublishedEventAsync(client);
        var (typed, e164) = NewPhone();

        using var response = await IdentityApi.CallAsync(client, HttpMethod.Post, Waitlist, Body(slug, typed, marketingConsent: true));
        var raw = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.Created, raw);
        var body = JsonDocument.Parse(raw).RootElement;
        var entryId = body.GetProperty("waitlistEntryId").GetGuid();
        Assert.True(body.GetProperty("smsSent").GetBoolean());

        await using var connection = await OpenTenantAsync();

        await using (var row = new NpgsqlCommand(
                         """
                         SELECT "Phone", "Name", "Gender", "MarketingConsent", "PrivacyPolicyVersion", "ConfirmationSentAt"
                         FROM ticketing.waitlist_entry WHERE "Id" = @id
                         """,
                         connection))
        {
            row.Parameters.AddWithValue("id", entryId);
            await using var reader = await row.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(e164, reader.GetString(0));
            Assert.Equal("Wanjiru Kamau", reader.GetString(1));
            Assert.Equal("NonBinary", reader.GetString(2));
            Assert.True(reader.GetBoolean(3));
            Assert.Equal("2026-10-01", reader.GetString(4));
            Assert.False(await reader.IsDBNullAsync(5));
        }

        var sms = Assert.Single(fixture.SmsGateway.SentTo(e164));
        Assert.Contains("KraftHaus 001", sms.Message, StringComparison.Ordinal);

        await using (var audit = new NpgsqlCommand(
                         """
                         SELECT "Action", "Metadata"::text, "TenantId" FROM ticketing.audit_log
                         WHERE "TargetType" = 'waitlist_entry' AND "TargetId" = @target ORDER BY "Action"
                         """,
                         connection))
        {
            audit.Parameters.AddWithValue("target", entryId.ToString("D"));
            await using var reader = await audit.ExecuteReaderAsync();

            var actions = new List<string>();
            while (await reader.ReadAsync())
            {
                actions.Add(reader.GetString(0));
                var metadata = reader.GetString(1);
                Assert.DoesNotContain(e164[4..], metadata, StringComparison.Ordinal);
                Assert.DoesNotContain("Wanjiru", metadata, StringComparison.Ordinal);
                Assert.Equal(fixture.TenantId, reader.GetGuid(2));
            }

            Assert.Equal(["ticketing.waitlist.confirmation_sent", "ticketing.waitlist.joined"], actions);
        }

        // Append-only, like every audit table.
        await using var tamper = new NpgsqlCommand("UPDATE ticketing.audit_log SET \"Action\" = 'x'", connection);
        await Assert.ThrowsAsync<PostgresException>(() => tamper.ExecuteNonQueryAsync());
    }

    [Fact]
    public async Task SamePhone_InAnotherShape_Returns409AlreadyJoined_AndSendsNoSecondSms()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var slug = await PublishedEventAsync(client);
        var (typed, e164) = NewPhone();

        using (var first = await IdentityApi.CallAsync(client, HttpMethod.Post, Waitlist, Body(slug, typed)))
        {
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        }

        await ExpectProblemAsync(client, slug, e164, HttpStatusCode.Conflict, "Ticketing.WaitlistAlreadyJoined");

        Assert.Single(fixture.SmsGateway.SentTo(e164));
        Assert.Equal(1L, await CountEntriesAsync(e164));
    }

    [Fact]
    public async Task InvalidForm_Returns400_WithFieldPrefixedErrors()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var slug = await PublishedEventAsync(client);

        using var response = await IdentityApi.CallAsync(client, HttpMethod.Post, Waitlist, new
        {
            eventSlug = slug,
            name = "W",
            phone = "0212345678",
            age = 12,
            gender = "woman",
            location = "Kilimani",
            email = "nope",
            privacyAccepted = false,
            privacyPolicyVersion = "2026-10-01",
            marketingConsent = false
        });
        var raw = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, raw);
        var problem = JsonDocument.Parse(raw).RootElement;
        Assert.Equal("Ticketing.WaitlistInvalid", problem.GetProperty("errorKey").GetString());

        var fields = problem.GetProperty("errors").EnumerateArray()
            .Select(x => x.GetString()!.Split(':')[0])
            .ToList();
        Assert.Equal(["Name", "Phone", "Age", "Email", "PrivacyAccepted"], fields);
    }

    [Fact]
    public async Task UnknownGenderValue_Returns400()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var slug = await PublishedEventAsync(client);

        using var response = await IdentityApi.CallAsync(client, HttpMethod.Post, Waitlist, new
        {
            eventSlug = slug,
            name = "Wanjiru Kamau",
            phone = NewPhone().Typed,
            age = 27,
            gender = "Woman ",
            location = "Kilimani",
            email = (string?)null,
            privacyAccepted = true,
            privacyPolicyVersion = "2026-10-01",
            marketingConsent = false
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DraftOrUnknownEvent_Returns404_CancelledEvent_Returns409Closed()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await TenantAdminAsync(client);

        var draftSlug = NewSlug();
        await CreateEventAsync(client, admin, draftSlug);
        await ExpectProblemAsync(client, draftSlug, NewPhone().Typed, HttpStatusCode.NotFound, "Ticketing.EventNotFound");
        await ExpectProblemAsync(client, "no-such-event", NewPhone().Typed, HttpStatusCode.NotFound, "Ticketing.EventNotFound");

        var (eventId, slug) = await PublishEventAsync(client, admin);
        await SendAsync(client, HttpMethod.Post, $"{Admin}/events/{eventId}/cancel", admin, new { reason = "Venue unavailable" }, HttpStatusCode.OK);

        var (typed, e164) = NewPhone();
        await ExpectProblemAsync(client, slug, typed, HttpStatusCode.Conflict, "Ticketing.WaitlistClosed");
        Assert.Equal(0L, await CountEntriesAsync(e164));
    }

    [Fact]
    public async Task SmsProviderOutage_StillJoins_AndReportsSmsNotSent()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var slug = await PublishedEventAsync(client);
        var (typed, e164) = NewPhone();

        fixture.SmsGateway.FailNext = true;
        using var response = await IdentityApi.CallAsync(client, HttpMethod.Post, Waitlist, Body(slug, typed));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.False((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("smsSent").GetBoolean());
        Assert.Equal(1L, await CountEntriesAsync(e164));
        Assert.Empty(fixture.SmsGateway.SentTo(e164));
    }

    [Fact]
    public async Task Waitlist_IsIsolatedPerTenant()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var slug = await PublishedEventAsync(client);

        // The same slug on the second tenant's host does not exist there.
        using var other = await IdentityApi.CallAsync(
            client,
            HttpMethod.Post,
            Waitlist,
            Body(slug, NewPhone().Typed),
            host: IdentityAuditApiFixture.SecondTenantHost);
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);

        await using var connection = new NpgsqlConnection(fixture.SecondTenantDatabaseConnectionString);
        await connection.OpenAsync();
        await using var count = new NpgsqlCommand("SELECT count(*) FROM ticketing.waitlist_entry", connection);
        Assert.Equal(0L, (long)(await count.ExecuteScalarAsync())!);
    }

    // -------------------------------------------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------------------------------------------

    private static object Body(string slug, string phone, bool marketingConsent = false) =>
        new
        {
            eventSlug = slug,
            name = "  Wanjiru Kamau ",
            phone,
            age = 27,
            gender = "non_binary",
            location = "Kilimani",
            email = (string?)null,
            privacyAccepted = true,
            privacyPolicyVersion = "2026-10-01",
            marketingConsent
        };

    /// <summary>A fresh number as a visitor types it (<c>07…</c>) and as it is stored (E.164).</summary>
    private static (string Typed, string E164) NewPhone()
    {
        var e164 = IdentityApi.NewKenyanPhone();
        return ("0" + e164[4..], e164);
    }

    private static async Task ExpectProblemAsync(
        HttpClient client,
        string slug,
        string phone,
        HttpStatusCode status,
        string errorKey)
    {
        using var response = await IdentityApi.CallAsync(client, HttpMethod.Post, Waitlist, Body(slug, phone));
        var raw = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == status, $"Expected {(int)status}, got {(int)response.StatusCode}: {raw}");
        Assert.Equal(errorKey, JsonDocument.Parse(raw).RootElement.GetProperty("errorKey").GetString());
    }

    private async Task<string> PublishedEventAsync(HttpClient client)
    {
        var admin = await TenantAdminAsync(client);
        return (await PublishEventAsync(client, admin)).Slug;
    }

    private static async Task<(Guid EventId, string Slug)> PublishEventAsync(HttpClient client, IdentityLogin admin)
    {
        var now = DateTimeOffset.UtcNow;
        var slug = NewSlug();
        var eventId = await CreateEventAsync(client, admin, slug);

        await SendAsync(client, HttpMethod.Put, $"{Admin}/events/{eventId}/venue/inline", admin,
            new { name = "KICC Grounds", city = "Nairobi" }, HttpStatusCode.OK);

        var startsAt = now.AddDays(20);
        var date = await SendAsync(client, HttpMethod.Post, $"{Admin}/events/{eventId}/dates", admin,
            new { label = (string?)null, startsAt, endsAt = startsAt.AddHours(8), doorsOpenAt = startsAt.AddHours(-1) },
            HttpStatusCode.Created);
        var dateId = date.GetProperty("id").GetGuid();

        var tier = await SendAsync(client, HttpMethod.Post, $"{Admin}/events/{eventId}/dates/{dateId}/tiers", admin,
            new { name = "Regular", capacity = 300 }, HttpStatusCode.Created);
        var tierId = tier.GetProperty("id").GetGuid();

        await SendAsync(client, HttpMethod.Post, $"{Admin}/events/{eventId}/dates/{dateId}/tiers/{tierId}/phases", admin,
            new { name = "Early Bird", price = 1000m, startsAt = now.AddDays(2), endsAt = now.AddDays(7), allocation = (int?)null },
            HttpStatusCode.Created);

        await SendAsync(client, HttpMethod.Post, $"{Admin}/events/{eventId}/publish", admin, null, HttpStatusCode.OK);

        return (eventId, slug);
    }

    private static async Task<Guid> CreateEventAsync(HttpClient client, IdentityLogin admin, string slug)
    {
        var created = await SendAsync(client, HttpMethod.Post, $"{Admin}/events", admin, new
        {
            slug,
            title = "KraftHaus 001",
            summary = "Food, drinks and music by Nairobi's best Krafts.",
            posterUrl = "https://cdn.example.com/posters/001.jpg"
        }, HttpStatusCode.Created);

        return created.GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> SendAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        IdentityLogin caller,
        object? body,
        HttpStatusCode expected)
    {
        using var response = await IdentityApi.CallAsync(client, method, path, body, caller.AccessToken);
        var content = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == expected, $"{method} {path} returned {(int)response.StatusCode}: {content}");

        return content.Length == 0 ? default : JsonDocument.Parse(content).RootElement.Clone();
    }

    private async Task<IdentityLogin> TenantAdminAsync(HttpClient client)
    {
        var login = await IdentityApi.SignInAsync(client, fixture.OtpSender);

        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        db.RoleAssignments.Add(RoleAssignment.Grant(
            new RoleAssignmentId(Guid.CreateVersion7()),
            new ApplicationUserId(login.UserId),
            IdentityRole.TenantAdmin,
            RoleScopeType.Tenant,
            fixture.TenantId,
            DateTimeOffset.UtcNow).Value!);

        await db.SaveChangesAsync();

        return login;
    }

    private async Task<NpgsqlConnection> OpenTenantAsync()
    {
        var connection = new NpgsqlConnection(fixture.TenantDatabaseConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private async Task<long> CountEntriesAsync(string e164)
    {
        await using var connection = await OpenTenantAsync();
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM ticketing.waitlist_entry WHERE \"Phone\" = @phone",
            connection);
        command.Parameters.AddWithValue("phone", e164);

        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static string NewSlug() => $"wl-{Guid.NewGuid():N}"[..20];
}
