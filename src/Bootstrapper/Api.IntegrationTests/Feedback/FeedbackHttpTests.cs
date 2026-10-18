using System.Net;
using System.Text.Json;
using Api.IntegrationTests.Identity.Auditing;
using Api.IntegrationTests.Ticketing;
using Feedback.Domain;
using Identity.Domain.Authorization;
using Identity.Domain.Roles;
using Npgsql;
using Xunit;

namespace Api.IntegrationTests.Feedback;

/// <summary>UF-010 / BR-027: checked-in guests rate once per date (changes allowed), anonymously; staff see scores, never authors.</summary>
[Collection(IdentityApiCollection.Name)]
public sealed class FeedbackHttpTests(IdentityAuditApiFixture fixture)
{
    private readonly TicketingApi _api = new(fixture);

    [Fact]
    public async Task CheckedInGuestsRateAnonymouslyOncePerDate()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.AdminAsync(client);
        var door = await _api.AdminAsync(client, IdentityRole.Support);
        var sale = await TicketingApi.PublishedEventAsync(client, admin);

        var guest = await _api.CustomerAsync(client);
        var tickets = await _api.BuyAsync(client, guest, sale, 2);
        var stayedHome = await _api.CustomerAsync(client);
        await _api.BuyAsync(client, stayedHome, sale, 1);

        var path = $"/api/client/events/{sale.Slug}/feedback";

        // Not checked in yet: nothing to rate.
        var before = await TicketingApi.SendAsync(client, HttpMethod.Get, path, guest, null, HttpStatusCode.OK);
        Assert.Equal(0, before.GetProperty("dates").GetArrayLength());

        // Both tickets admitted (a guest holding two tickets still rates once).
        foreach (var ticket in tickets)
        {
            await TicketingApi.SendAsync(client, HttpMethod.Post, "/api/ops/check-in/manual", door,
                new { eventDateId = sale.DateId, code = ticket.GetProperty("code").GetString(), ticketId = (Guid?)null }, HttpStatusCode.OK);
        }

        var mine = await TicketingApi.SendAsync(client, HttpMethod.Get, path, guest, null, HttpStatusCode.OK);
        var date = Assert.Single(mine.GetProperty("dates").EnumerateArray());
        Assert.True(date.GetProperty("open").GetBoolean());
        Assert.Equal(JsonValueKind.Null, date.GetProperty("mine").ValueKind);

        await TicketingApi.SendAsync(client, HttpMethod.Put, $"{path}/{sale.DateId}", guest,
            new { overall = 7, music = (int?)null, food = (int?)null, venue = (int?)null, organisation = (int?)null, comment = (string?)null }, HttpStatusCode.BadRequest);
        await TicketingApi.SendAsync(client, HttpMethod.Put, $"{path}/{sale.DateId}", guest,
            new { overall = 4, music = 5, food = 3, venue = (int?)null, organisation = (int?)null, comment = "Great DJ, long food queue" }, HttpStatusCode.OK);

        // Changing it keeps one rating.
        var changed = await TicketingApi.SendAsync(client, HttpMethod.Put, $"{path}/{sale.DateId}", guest,
            new { overall = 5, music = 5, food = 3, venue = 4, organisation = (int?)null, comment = "Great DJ, long food queue" }, HttpStatusCode.OK);
        Assert.Equal(5, changed.GetProperty("overall").GetInt32());

        // Someone who never came in cannot rate.
        await TicketingApi.SendAsync(client, HttpMethod.Put, $"{path}/{sale.DateId}", stayedHome,
            new { overall = 1, music = (int?)null, food = (int?)null, venue = (int?)null, organisation = (int?)null, comment = "x" }, HttpStatusCode.Forbidden);

        var summary = await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/admin/events/{sale.EventId}/feedback", door, null, HttpStatusCode.OK);
        Assert.Equal(1, summary.GetProperty("overall").GetProperty("ratings").GetInt32());
        Assert.Equal(5m, summary.GetProperty("overall").GetProperty("average").GetDecimal());
        Assert.Equal(4m, summary.GetProperty("overall").GetProperty("aspectAverages").GetProperty("Venue").GetDecimal());

        var comments = await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/admin/events/{sale.EventId}/feedback/comments", door, null, HttpStatusCode.OK);
        var comment = Assert.Single(comments.EnumerateArray());
        Assert.DoesNotContain(guest.Phone, comments.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(guest.UserId.ToString(), comments.GetRawText(), StringComparison.OrdinalIgnoreCase);

        // Support reads but cannot hide; the Tenant Admin can.
        var ratingId = comment.GetProperty("id").GetGuid();
        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/feedback/{ratingId}/hide", door, null, HttpStatusCode.Forbidden);
        var hidden = await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/feedback/{ratingId}/hide", admin, null, HttpStatusCode.OK);
        Assert.True(hidden.GetProperty("hidden").GetBoolean());

        // Nothing on the row identifies the guest.
        await using var connection = await _api.OpenTenantAsync();
        await using var command = new NpgsqlCommand("SELECT row_to_json(r)::text FROM feedback.rating r", connection);
        var row = (string)(await command.ExecuteScalarAsync())!;
        Assert.DoesNotContain(guest.Phone, row, StringComparison.Ordinal);
        Assert.DoesNotContain(guest.UserId.ToString(), row, StringComparison.OrdinalIgnoreCase);
        foreach (var ticket in tickets)
        {
            Assert.DoesNotContain(ticket.GetProperty("id").GetGuid().ToString(), row, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void FeedbackPermissionsAreInIdentitysCatalog() =>
        Assert.Subset(IdentityPermission.All.ToHashSet(StringComparer.Ordinal), FeedbackPermission.All.ToHashSet(StringComparer.Ordinal));
}
