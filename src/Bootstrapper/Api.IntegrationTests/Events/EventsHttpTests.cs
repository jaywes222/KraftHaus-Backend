using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.IntegrationTests.Identity.Auditing;
using Common.Domain.StronglyTypedIds;
using Events.Domain.Common;
using Identity.Domain.Authorization;
using Identity.Domain.Common;
using Identity.Domain.Roles;
using Identity.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Api.IntegrationTests.Events;

/// <summary>
///     UF-016 through the real host and a real tenant database: venue library, event setup (venue copy, dates,
///     per-date tiers, sale phases, Couple / Gang offers, policies), publishing, the public client view, the
///     post-publish protections, permissions, tenant isolation and the audit trail.
/// </summary>
[Collection(IdentityApiCollection.Name)]
public sealed class EventsHttpTests(IdentityAuditApiFixture fixture)
{
    private const string Admin = "/api/admin";
    private const string Client = "/api/client/events";

    [Fact]
    public async Task FullSetup_Publish_ShowsCurrentPricesAndOffersToCustomers()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await TenantAdminAsync(client);
        var now = DateTimeOffset.UtcNow;

        // Library venue with two entrances.
        var venue = await SendAsync(client, HttpMethod.Post, $"{Admin}/venues", admin, new
        {
            name = "Uhuru Gardens",
            addressLine = "Langata Road",
            city = "Nairobi",
            mapUrl = "https://maps.example.com/uhuru",
            capacity = 5000
        }, HttpStatusCode.Created);
        var venueId = venue.GetProperty("id").GetGuid();

        await SendAsync(client, HttpMethod.Post, $"{Admin}/venues/{venueId}/entrances", admin, new { name = "Main gate" }, HttpStatusCode.Created);
        await SendAsync(client, HttpMethod.Post, $"{Admin}/venues/{venueId}/entrances", admin, new { name = "VIP gate", description = "Langata Rd side" }, HttpStatusCode.Created);

        // Event with the library venue copied on, then edited inline (the library venue stays untouched).
        var slug = NewSlug();
        var eventId = await CreateEventAsync(client, admin, slug);

        var withVenue = await SendAsync(client, HttpMethod.Put, $"{Admin}/events/{eventId}/venue/library", admin, new { venueId }, HttpStatusCode.OK);
        Assert.Equal(2, withVenue.GetProperty("entrances").GetArrayLength());
        Assert.Equal(venueId, withVenue.GetProperty("venue").GetProperty("sourceVenueId").GetGuid());

        await SendAsync(client, HttpMethod.Put, $"{Admin}/events/{eventId}/venue", admin, new
        {
            name = "Uhuru Gardens",
            addressLine = "Langata Road",
            city = "Nairobi",
            directions = "Use the Langata Rd entrance; parking opens at 4pm."
        }, HttpStatusCode.OK);

        var library = await SendAsync(client, HttpMethod.Get, $"{Admin}/venues/{venueId}", admin, null, HttpStatusCode.OK);
        Assert.Equal(JsonValueKind.Null, library.GetProperty("directions").ValueKind);

        // Two dates, each with their own tiers and prices.
        var day1 = await AddDateAsync(client, admin, eventId, "Day 1", now.AddDays(30));
        var day2 = await AddDateAsync(client, admin, eventId, "Day 2", now.AddDays(31));

        var regular = await AddTierAsync(client, admin, eventId, day1, "Regular", 500);
        var vip = await AddTierAsync(client, admin, eventId, day1, "VIP", 50);
        var day2Regular = await AddTierAsync(client, admin, eventId, day2, "Regular", 400);

        await AddPhaseAsync(client, admin, eventId, day1, regular, "Early Bird", 1500, now.AddHours(-1), now.AddDays(7), 100);
        await AddPhaseAsync(client, admin, eventId, day1, regular, "Advance", 2000, now.AddDays(7), now.AddDays(30));
        await AddPhaseAsync(client, admin, eventId, day1, vip, "VIP", 6000, now.AddHours(-1), now.AddDays(30));
        await AddPhaseAsync(client, admin, eventId, day2, day2Regular, "General", 1800, now.AddHours(-1), now.AddDays(31));

        await AddOfferAsync(client, admin, eventId, day1, regular, "Couple", "Couple ticket", 2, 2800, now.AddHours(-1), now.AddDays(30));
        await AddOfferAsync(client, admin, eventId, day1, regular, "Gang", "Gang Gang (5)", 5, 6500, now.AddHours(-1), now.AddDays(30));

        await SendAsync(client, HttpMethod.Put, $"{Admin}/events/{eventId}/policies", admin, new
        {
            refundsEnabled = false,
            transfersEnabled = true,
            maxTransfersPerTicket = 1,
            lipaMdogoMdogoEnabled = true,
            holdMinutes = 10,
            maxTicketsPerOrder = 8
        }, HttpStatusCode.OK);

        // Draft: not visible to customers.
        using (var draft = await IdentityApi.CallAsync(client, HttpMethod.Get, $"{Client}/{slug}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, draft.StatusCode);
        }

        var published = await SendAsync(client, HttpMethod.Post, $"{Admin}/events/{eventId}/publish", admin, null, HttpStatusCode.OK);
        Assert.Equal("Published", published.GetProperty("status").GetString());

        // Public list: cheapest current price across tiers is the "from" price.
        using (var list = await IdentityApi.CallAsync(client, HttpMethod.Get, Client))
        {
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            var cards = await list.Content.ReadFromJsonAsync<JsonElement>();
            var card = cards.EnumerateArray().Single(x => x.GetProperty("slug").GetString() == slug);
            Assert.Equal(1500m, card.GetProperty("fromPrice").GetProperty("amount").GetDecimal());
            Assert.Equal("KES", card.GetProperty("fromPrice").GetProperty("currency").GetString());
        }

        // Public event: current and next price, offers on sale, and no capacities or allocations.
        using var detail = await IdentityApi.CallAsync(client, HttpMethod.Get, $"{Client}/{slug}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var raw = await detail.Content.ReadAsStringAsync();
        Assert.DoesNotContain("capacity", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("allocation", raw, StringComparison.OrdinalIgnoreCase);

        var publicEvent = JsonDocument.Parse(raw).RootElement;
        Assert.Equal(2, publicEvent.GetProperty("dates").GetArrayLength());
        Assert.Contains("parking", publicEvent.GetProperty("venue").GetProperty("directions").GetString(), StringComparison.Ordinal);

        var publicRegular = publicEvent.GetProperty("dates")[0].GetProperty("tiers").EnumerateArray()
            .Single(x => x.GetProperty("id").GetGuid() == regular);
        Assert.True(publicRegular.GetProperty("onSale").GetBoolean());
        Assert.Equal("Early Bird", publicRegular.GetProperty("currentPrice").GetProperty("phaseName").GetString());
        Assert.Equal(1500m, publicRegular.GetProperty("currentPrice").GetProperty("price").GetProperty("amount").GetDecimal());
        Assert.Equal("Advance", publicRegular.GetProperty("nextPrice").GetProperty("phaseName").GetString());

        var offers = publicRegular.GetProperty("groupOffers").EnumerateArray().ToList();
        Assert.Equal(["Couple", "Gang"], offers.Select(x => x.GetProperty("kind").GetString()));
        Assert.Equal(5, offers[1].GetProperty("admits").GetInt32());

        // Audit trail in the tenant database, attributed to the admin.
        Assert.Equal(1, await AuditCountAsync("events.event.published", eventId, admin.UserId));
        Assert.Equal(2, await AuditCountAsync("events.event.date_added", eventId, admin.UserId));
        Assert.Equal(2, await AuditCountAsync("events.event.group_offer_added", eventId, admin.UserId));
    }

    [Fact]
    public async Task Publish_IncompleteEvent_Returns409WithEveryProblem()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await TenantAdminAsync(client);
        var eventId = await CreateEventAsync(client, admin, NewSlug());
        var date = await AddDateAsync(client, admin, eventId, "Opening night", DateTimeOffset.UtcNow.AddDays(10));
        await AddTierAsync(client, admin, eventId, date, "Regular", 100);

        using var response = await IdentityApi.CallAsync(
            client, HttpMethod.Post, $"{Admin}/events/{eventId}/publish", accessToken: admin.AccessToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadAsStringAsync();
        Assert.Contains("Events.NotReadyToPublish", problem, StringComparison.Ordinal);
        Assert.Contains("Set a venue.", problem, StringComparison.Ordinal);
        Assert.Contains("has no sale phase or group offer", problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishedEvent_ProtectsWhatCustomersMayHaveBought()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await TenantAdminAsync(client);
        var (eventId, slug, date, tier, phase) = await PublishedEventAsync(client, admin);
        var now = DateTimeOffset.UtcNow;

        await ExpectErrorAsync(client, HttpMethod.Delete, $"{Admin}/events/{eventId}/dates/{date}/tiers/{tier}", admin, null, "Events.TierFrozen");
        await ExpectErrorAsync(client, HttpMethod.Put, $"{Admin}/events/{eventId}/dates/{date}/tiers/{tier}", admin,
            new { name = "Regular", capacity = 10 }, "Events.CapacityReduced");
        await ExpectErrorAsync(client, HttpMethod.Put, $"{Admin}/events/{eventId}/dates/{date}/tiers/{tier}/phases/{phase}", admin,
            new { name = "Early Bird", price = 999, startsAt = now.AddHours(-1), endsAt = now.AddDays(7) }, "Events.PhaseStarted");
        await ExpectErrorAsync(client, HttpMethod.Put, $"{Admin}/events/{eventId}", admin,
            new { slug = NewSlug(), title = "Renamed" }, "Events.SlugFrozen");

        // Growing capacity is fine; withdrawing hides the tier from customers.
        await SendAsync(client, HttpMethod.Put, $"{Admin}/events/{eventId}/dates/{date}/tiers/{tier}", admin,
            new { name = "Regular", capacity = 1000 }, HttpStatusCode.OK);

        var withdrawn = await SendAsync(client, HttpMethod.Post, $"{Admin}/events/{eventId}/dates/{date}/tiers/{tier}/withdraw", admin, null, HttpStatusCode.OK);
        Assert.Equal("Withdrawn", withdrawn.GetProperty("dates")[0].GetProperty("tiers")[0].GetProperty("status").GetString());

        using var publicView = await IdentityApi.CallAsync(client, HttpMethod.Get, $"{Client}/{slug}");
        var dates = (await publicView.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("dates");
        Assert.Equal(0, dates[0].GetProperty("tiers").GetArrayLength());

        // Cancel: visible by link with its reason, gone from the list, read-only afterwards.
        await SendAsync(client, HttpMethod.Post, $"{Admin}/events/{eventId}/cancel", admin, new { reason = "Venue unavailable" }, HttpStatusCode.OK);

        using (var cancelled = await IdentityApi.CallAsync(client, HttpMethod.Get, $"{Client}/{slug}"))
        {
            var body = await cancelled.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("Cancelled", body.GetProperty("status").GetString());
            Assert.Equal("Venue unavailable", body.GetProperty("cancellationReason").GetString());
        }

        await ExpectErrorAsync(client, HttpMethod.Post, $"{Admin}/events/{eventId}/dates", admin,
            new { startsAt = now.AddDays(40), endsAt = now.AddDays(40).AddHours(5) }, "Events.EventClosed");
    }

    [Fact]
    public async Task Permissions_SupportCanViewButNotManage_AnonymousIsRejected()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await TenantAdminAsync(client);
        var eventId = await CreateEventAsync(client, admin, NewSlug());

        var support = await IdentityApi.SignInAsync(client, fixture.OtpSender);
        await GrantAsync(support.UserId, IdentityRole.Support, fixture.TenantId);

        await SendAsync(client, HttpMethod.Get, $"{Admin}/events/{eventId}", support, null, HttpStatusCode.OK);

        using (var manage = await IdentityApi.CallAsync(
                   client, HttpMethod.Post, $"{Admin}/events/{eventId}/publish", accessToken: support.AccessToken))
        {
            Assert.Equal(HttpStatusCode.Forbidden, manage.StatusCode);
        }

        using (var anonymous = await IdentityApi.CallAsync(client, HttpMethod.Get, $"{Admin}/events"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }
    }

    [Fact]
    public async Task Events_AreIsolatedPerTenantDatabase()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await TenantAdminAsync(client);
        var slug = NewSlug();
        var eventId = await CreateEventAsync(client, admin, slug);

        var otherAdmin = await SecondTenantAdminAsync(client);

        using (var list = await IdentityApi.CallAsync(
                   client, HttpMethod.Get, $"{Admin}/events", accessToken: otherAdmin, host: IdentityAuditApiFixture.SecondTenantHost))
        {
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            var events = await list.Content.ReadFromJsonAsync<JsonElement>();
            Assert.DoesNotContain(events.EnumerateArray(), x => x.GetProperty("id").GetGuid() == eventId);
        }

        using (var get = await IdentityApi.CallAsync(
                   client, HttpMethod.Get, $"{Admin}/events/{eventId}", accessToken: otherAdmin, host: IdentityAuditApiFixture.SecondTenantHost))
        {
            Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        }

        // The same slug is free on the other tenant: uniqueness is per tenant database.
        using var create = await IdentityApi.CallAsync(
            client,
            HttpMethod.Post,
            $"{Admin}/events",
            new { slug, title = "Other tenant's event" },
            otherAdmin,
            IdentityAuditApiFixture.SecondTenantHost);
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        // A first-tenant token is refused on the second tenant's host.
        using var crossTenant = await IdentityApi.CallAsync(
            client, HttpMethod.Get, $"{Admin}/events", accessToken: admin.AccessToken, host: IdentityAuditApiFixture.SecondTenantHost);
        Assert.Equal(HttpStatusCode.Forbidden, crossTenant.StatusCode);
    }

    [Fact]
    public async Task DuplicateSlug_Returns409_AndAuditLogIsAppendOnly()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await TenantAdminAsync(client);
        var slug = NewSlug();
        await CreateEventAsync(client, admin, slug);

        await ExpectErrorAsync(client, HttpMethod.Post, $"{Admin}/events", admin, new { slug, title = "Again" }, "Events.SlugTaken");

        await using var connection = new NpgsqlConnection(fixture.TenantDatabaseConnectionString);
        await connection.OpenAsync();
        await using var update = new NpgsqlCommand("UPDATE events.audit_log SET \"Action\" = 'tampered'", connection);

        var error = await Assert.ThrowsAsync<PostgresException>(() => update.ExecuteNonQueryAsync());
        Assert.Contains("append-only", error.MessageText, StringComparison.Ordinal);
    }

    [Fact]
    public void EventsPermissions_AreInTheIdentityCatalog()
    {
        foreach (var permission in EventsPermission.All)
        {
            Assert.Contains(permission, IdentityPermission.All);
        }

        Assert.True(IdentityPermissionMatrix.HasPermission(IdentityRole.TenantAdmin, EventsPermission.Publish));
        Assert.True(IdentityPermissionMatrix.HasPermission(IdentityRole.Support, EventsPermission.View));
        Assert.False(IdentityPermissionMatrix.HasPermission(IdentityRole.Support, EventsPermission.Manage));
        Assert.False(IdentityPermissionMatrix.HasPermission(IdentityRole.KraftStaff, EventsPermission.View));
    }

    // -------------------------------------------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------------------------------------------

    private static async Task<(Guid EventId, string Slug, Guid Date, Guid Tier, Guid Phase)> PublishedEventAsync(
        HttpClient client,
        IdentityLogin admin)
    {
        var now = DateTimeOffset.UtcNow;
        var slug = NewSlug();
        var eventId = await CreateEventAsync(client, admin, slug);

        await SendAsync(client, HttpMethod.Put, $"{Admin}/events/{eventId}/venue/inline", admin,
            new { name = "KICC Grounds", city = "Nairobi" }, HttpStatusCode.OK);

        var date = await AddDateAsync(client, admin, eventId, null, now.AddDays(20));
        var tier = await AddTierAsync(client, admin, eventId, date, "Regular", 300);
        var phase = await AddPhaseAsync(client, admin, eventId, date, tier, "Early Bird", 1000, now.AddHours(-1), now.AddDays(7));

        await SendAsync(client, HttpMethod.Post, $"{Admin}/events/{eventId}/publish", admin, null, HttpStatusCode.OK);

        return (eventId, slug, date, tier, phase);
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

        Assert.Equal("Draft", created.GetProperty("status").GetString());

        return created.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> AddDateAsync(
        HttpClient client,
        IdentityLogin admin,
        Guid eventId,
        string? label,
        DateTimeOffset startsAt)
    {
        var created = await SendAsync(client, HttpMethod.Post, $"{Admin}/events/{eventId}/dates", admin, new
        {
            label,
            startsAt,
            endsAt = startsAt.AddHours(8),
            doorsOpenAt = startsAt.AddHours(-1)
        }, HttpStatusCode.Created);

        return created.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> AddTierAsync(
        HttpClient client,
        IdentityLogin admin,
        Guid eventId,
        Guid dateId,
        string name,
        int capacity)
    {
        var created = await SendAsync(
            client,
            HttpMethod.Post,
            $"{Admin}/events/{eventId}/dates/{dateId}/tiers",
            admin,
            new { name, capacity },
            HttpStatusCode.Created);

        return created.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> AddPhaseAsync(
        HttpClient client,
        IdentityLogin admin,
        Guid eventId,
        Guid dateId,
        Guid tierId,
        string name,
        decimal price,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt,
        int? allocation = null)
    {
        var created = await SendAsync(
            client,
            HttpMethod.Post,
            $"{Admin}/events/{eventId}/dates/{dateId}/tiers/{tierId}/phases",
            admin,
            new { name, price, startsAt, endsAt, allocation },
            HttpStatusCode.Created);

        return created.GetProperty("id").GetGuid();
    }

    private static async Task AddOfferAsync(
        HttpClient client,
        IdentityLogin admin,
        Guid eventId,
        Guid dateId,
        Guid tierId,
        string kind,
        string name,
        int admits,
        decimal price,
        DateTimeOffset startsAt,
        DateTimeOffset endsAt) =>
        await SendAsync(
            client,
            HttpMethod.Post,
            $"{Admin}/events/{eventId}/dates/{dateId}/tiers/{tierId}/offers",
            admin,
            new { kind, name, admits, price, startsAt, endsAt },
            HttpStatusCode.Created);

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

    private static async Task ExpectErrorAsync(
        HttpClient client,
        HttpMethod method,
        string path,
        IdentityLogin caller,
        object? body,
        string errorKey)
    {
        using var response = await IdentityApi.CallAsync(client, method, path, body, caller.AccessToken);
        var content = await response.Content.ReadAsStringAsync();

        Assert.True(
            response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.BadRequest,
            $"{method} {path} returned {(int)response.StatusCode}: {content}");
        Assert.Contains(errorKey, content, StringComparison.Ordinal);
    }

    private async Task<IdentityLogin> TenantAdminAsync(HttpClient client)
    {
        var login = await IdentityApi.SignInAsync(client, fixture.OtpSender);
        await GrantAsync(login.UserId, IdentityRole.TenantAdmin, fixture.TenantId);
        return login;
    }

    private async Task<string> SecondTenantAdminAsync(HttpClient client)
    {
        var phone = IdentityApi.NewKenyanPhone();
        var host = IdentityAuditApiFixture.SecondTenantHost;

        using var request = await IdentityApi.CallAsync(
            client,
            HttpMethod.Post,
            "/api/identity/otp/request",
            new { channel = "Phone", destination = phone, purpose = "SignIn" },
            host: host);
        Assert.Equal(HttpStatusCode.OK, request.StatusCode);
        var challengeId = (await request.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("challengeId").GetString();

        using var verify = await IdentityApi.CallAsync(
            client,
            HttpMethod.Post,
            "/api/identity/otp/verify",
            new
            {
                challengeId,
                code = fixture.OtpSender.CodeFor(phone),
                deviceId = IdentityApi.DeviceId,
                displayName = IdentityApi.DisplayName,
                acceptTerms = true,
                acceptPrivacy = true
            },
            host: host);
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);

        var body = await verify.Content.ReadFromJsonAsync<JsonElement>();
        await GrantAsync(body.GetProperty("userId").GetGuid(), IdentityRole.TenantAdmin, fixture.SecondTenantId);

        return body.GetProperty("accessToken").GetString()!;
    }

    private async Task GrantAsync(Guid userId, IdentityRole role, Guid tenantId)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();

        db.RoleAssignments.Add(RoleAssignment.Grant(
            new RoleAssignmentId(Guid.CreateVersion7()),
            new ApplicationUserId(userId),
            role,
            RoleScopeType.Tenant,
            tenantId,
            DateTimeOffset.UtcNow).Value!);

        await db.SaveChangesAsync();
    }

    private async Task<long> AuditCountAsync(string action, Guid eventId, Guid actorId)
    {
        await using var connection = new NpgsqlConnection(fixture.TenantDatabaseConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT count(*) FROM events.audit_log
            WHERE "Action" = @action AND "TargetType" = 'event' AND "TargetId" = @target
              AND "ActorType" = 'User' AND "ActorId" = @actor AND "TenantId" = @tenant
            """,
            connection);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("target", eventId.ToString("D", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("actor", actorId.ToString("D", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("tenant", fixture.TenantId);

        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static string NewSlug() => $"it-{Guid.NewGuid():N}"[..20];
}
