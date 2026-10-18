using System.Net;
using System.Text.Json;
using Api.IntegrationTests.Identity.Auditing;
using Api.IntegrationTests.Ticketing;
using Identity.Domain.Authorization;
using Identity.Domain.Roles;
using Music.Domain.Common;
using Npgsql;
using Xunit;

namespace Api.IntegrationTests.Music;

/// <summary>
///     Song requests end to end: the DJ opens requests; ticket holders search, request and second songs within their
///     limit; non-holders are refused; the DJ works the queue; the public list shows songs and votes, never names.
/// </summary>
[Collection(IdentityApiCollection.Name)]
public sealed class SongRequestsHttpTests(IdentityAuditApiFixture fixture)
{
    private readonly TicketingApi _api = new(fixture);

    [Fact]
    public async Task TicketHoldersRequestSongsAndTheDjWorksTheQueue()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.AdminAsync(client);
        var dj = await _api.AdminAsync(client, IdentityRole.Dj);
        var sale = await TicketingApi.PublishedEventAsync(client, admin);

        var alice = await _api.CustomerAsync(client);
        var bob = await _api.CustomerAsync(client);
        var stranger = await _api.CustomerAsync(client);
        await _api.BuyAsync(client, alice, sale, 1);
        await _api.BuyAsync(client, bob, sale, 1);

        var publicPath = $"/api/client/events/{sale.Slug}/song-requests";
        var consolePath = $"/api/admin/events/{sale.EventId}/song-requests";

        // Open from publication, before the night, three each by default.
        var before = await Anonymous(client, publicPath);
        Assert.True(before.GetProperty("open").GetBoolean());
        Assert.Equal(3, before.GetProperty("maxPerPerson").GetInt32());
        var console = await TicketingApi.SendAsync(client, HttpMethod.Get, consolePath, dj, null, HttpStatusCode.OK);
        Assert.True(console.GetProperty("board").GetProperty("open").GetBoolean());

        // The DJ can close it...
        await TicketingApi.SendAsync(client, HttpMethod.Put, $"{consolePath}/board", dj,
            new { open = false, maxPerPerson = 3, note = (string?)null }, HttpStatusCode.OK);
        await Request(client, alice, publicPath, new { trackId = FakeMusicCatalog.Suzanna.Id }, HttpStatusCode.Conflict, "Music.RequestsClosed");

        // ...and reopen it with a limit of 2 and a note.
        await TicketingApi.SendAsync(client, HttpMethod.Put, $"{consolePath}/board", dj,
            new { open = true, maxPerPerson = 2, note = "DJ Kace takes requests until 23:00" }, HttpStatusCode.OK);

        // Search needs an account; it goes through the catalogue.
        using (var anonymousSearch = await IdentityApi.CallAsync(client, HttpMethod.Get, "/api/client/music/search?q=sauti"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousSearch.StatusCode);
        }

        var search = await TicketingApi.SendAsync(client, HttpMethod.Get, "/api/client/music/search?q=sauti", alice, null, HttpStatusCode.OK);
        var hit = Assert.Single(search.EnumerateArray());
        Assert.Equal("Suzanna", hit.GetProperty("title").GetString());
        Assert.EndsWith("/front-250", hit.GetProperty("coverUrl").GetString(), StringComparison.Ordinal);

        // Alice requests it; Bob asks for the same song: one request, two votes.
        var first = await Request(client, alice, publicPath, new { trackId = FakeMusicCatalog.Suzanna.Id }, HttpStatusCode.Created);
        Assert.False(first.GetProperty("seconded").GetBoolean());
        var requestId = first.GetProperty("request").GetProperty("id").GetGuid();

        var second = await Request(client, bob, publicPath, new { trackId = FakeMusicCatalog.Suzanna.Id }, HttpStatusCode.Created);
        Assert.True(second.GetProperty("seconded").GetBoolean());
        Assert.Equal(requestId, second.GetProperty("request").GetProperty("id").GetGuid());
        Assert.Equal(2, second.GetProperty("request").GetProperty("votes").GetInt32());

        await Request(client, bob, publicPath, new { trackId = FakeMusicCatalog.Suzanna.Id }, HttpStatusCode.Conflict, "Music.AlreadyRequested");
        await Request(client, bob, $"{publicPath}/{requestId}/votes", new { }, HttpStatusCode.Conflict, "Music.AlreadyRequested");

        // A song that isn't in the catalogue can be typed; the same song typed differently is the same request.
        var typed = await Request(client, alice, publicPath, new { title = "Wamlambez", artist = "Sailors Gang" }, HttpStatusCode.Created);
        var typedAgain = await Request(client, bob, publicPath, new { title = "  wamlambez!", artist = "SAILORS  gang" }, HttpStatusCode.Created);
        Assert.Equal(typed.GetProperty("request").GetProperty("id").GetGuid(), typedAgain.GetProperty("request").GetProperty("id").GetGuid());

        // Alice has used both of hers.
        await Request(client, alice, publicPath, new { trackId = FakeMusicCatalog.Unavailable.Id }, HttpStatusCode.Conflict, "Music.LimitReached");
        var mine = await TicketingApi.SendAsync(client, HttpMethod.Get, $"{publicPath}/mine", alice, null, HttpStatusCode.OK);
        Assert.Equal("LimitReached", mine.GetProperty("eligibility").GetString());
        Assert.Equal(2, mine.GetProperty("used").GetInt32());
        Assert.Equal(2, mine.GetProperty("requestIds").GetArrayLength());

        // No ticket, no request; an unknown catalogue id is refused; the body cannot invent catalogue details.
        await Request(client, stranger, publicPath, new { trackId = FakeMusicCatalog.Suzanna.Id }, HttpStatusCode.Forbidden, "Music.TicketRequired");
        var strangerMine = await TicketingApi.SendAsync(client, HttpMethod.Get, $"{publicPath}/mine", stranger, null, HttpStatusCode.OK);
        Assert.Equal("TicketRequired", strangerMine.GetProperty("eligibility").GetString());

        var charlie = await _api.CustomerAsync(client);
        await _api.BuyAsync(client, charlie, sale, 1);
        await Request(client, charlie, publicPath, new { trackId = Guid.NewGuid().ToString(), title = "Fake", artist = "Fake" }, HttpStatusCode.NotFound, "Music.TrackNotFound");

        // "Me too" on a song already on the list; an id from another event is not found.
        var metoo = await Request(client, charlie, $"{publicPath}/{typed.GetProperty("request").GetProperty("id").GetGuid()}/votes", new { }, HttpStatusCode.Created);
        Assert.Equal(3, metoo.GetProperty("request").GetProperty("votes").GetInt32());
        await Request(client, charlie, $"{publicPath}/{Guid.NewGuid()}/votes", new { }, HttpStatusCode.NotFound, "Music.RequestNotFound");

        // The DJ console shows votes and first names; the public list shows neither names nor accounts.
        console = await TicketingApi.SendAsync(client, HttpMethod.Get, consolePath, dj, null, HttpStatusCode.OK);
        var requests = console.GetProperty("requests").EnumerateArray().ToList();
        Assert.Equal(2, requests.Count);
        Assert.Equal(3, requests[0].GetProperty("votes").GetInt32());
        Assert.Equal(3, requests[0].GetProperty("requestedBy").GetArrayLength());

        var publicList = await Anonymous(client, publicPath);
        Assert.Equal("DJ Kace takes requests until 23:00", publicList.GetProperty("note").GetString());
        Assert.Equal(2, publicList.GetProperty("queue").GetArrayLength());
        Assert.DoesNotContain("requestedBy", publicList.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(alice.Phone, publicList.GetRawText(), StringComparison.Ordinal);

        // Up next → played: off the queue, into "Recently played"; it can't be seconded any more but can be undone.
        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/song-requests/{requestId}/status", dj, new { status = "UpNext" }, HttpStatusCode.OK);
        publicList = await Anonymous(client, publicPath);
        Assert.Equal("UpNext", publicList.GetProperty("queue")[0].GetProperty("status").GetString());

        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/song-requests/{requestId}/status", dj, new { status = "Played" }, HttpStatusCode.OK);
        publicList = await Anonymous(client, publicPath);
        Assert.Single(publicList.GetProperty("queue").EnumerateArray());
        Assert.Equal(requestId, Assert.Single(publicList.GetProperty("recentlyPlayed").EnumerateArray()).GetProperty("id").GetGuid());

        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/song-requests/{requestId}/status", dj, new { status = "Declined" }, HttpStatusCode.Conflict);

        // Customers cannot reach the console; the DJ cannot reach event setup writes.
        await TicketingApi.SendAsync(client, HttpMethod.Get, consolePath, alice, null, HttpStatusCode.Forbidden);
        await TicketingApi.SendAsync(client, HttpMethod.Put, $"/api/admin/events/{sale.EventId}/policies", dj,
            new { refundsEnabled = false, transfersEnabled = true, maxTransfersPerTicket = 1, lipaMdogoMdogoEnabled = true, holdMinutes = 10, maxTicketsPerOrder = 10 },
            HttpStatusCode.Forbidden);

        // Closing stops requests.
        await TicketingApi.SendAsync(client, HttpMethod.Put, $"{consolePath}/board", dj, new { open = false, maxPerPerson = 2, note = (string?)null }, HttpStatusCode.OK);
        await Request(client, charlie, publicPath, new { trackId = FakeMusicCatalog.Unavailable.Id }, HttpStatusCode.Conflict, "Music.RequestsClosed");

        // Every change is audited, without names or phone numbers.
        await using var connection = new NpgsqlConnection(fixture.TenantDatabaseConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """SELECT "Action", "Metadata"::text FROM music.audit_log WHERE "Metadata"->>'eventId' = @eventId ORDER BY "OccurredAt" """,
            connection);
        command.Parameters.AddWithValue("eventId", sale.EventId.ToString("D"));
        var rows = new List<(string Action, string Metadata)>();
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                rows.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        Assert.Contains(rows, x => x.Action == "music.board.opened");
        Assert.Equal(2, rows.Count(x => x.Action == "music.request.created"));
        Assert.Equal(3, rows.Count(x => x.Action == "music.request.seconded"));
        Assert.Contains(rows, x => x.Action == "music.board.closed");
        Assert.DoesNotContain(rows, x => x.Metadata.Contains(alice.Phone, StringComparison.Ordinal));
    }

    [Fact]
    public async Task WhenTheCatalogueIsDownTheSongCanStillBeTyped()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.AdminAsync(client);
        var sale = await TicketingApi.PublishedEventAsync(client, admin);
        var fan = await _api.CustomerAsync(client);
        await _api.BuyAsync(client, fan, sale, 1);

        await TicketingApi.SendAsync(client, HttpMethod.Put, $"/api/admin/events/{sale.EventId}/song-requests/board", admin,
            new { open = true, maxPerPerson = 3, note = (string?)null }, HttpStatusCode.OK);

        fixture.MusicCatalog.Offline = true;
        try
        {
            using var search = await IdentityApi.CallAsync(client, HttpMethod.Get, "/api/client/music/search?q=sauti", accessToken: fan.AccessToken);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, search.StatusCode);
            Assert.Contains("Music.CatalogUnavailable", await search.Content.ReadAsStringAsync(), StringComparison.Ordinal);

            await Request(client, fan, $"/api/client/events/{sale.Slug}/song-requests", new { title = "Suzanna", artist = "Sauti Sol" }, HttpStatusCode.Created);
        }
        finally
        {
            fixture.MusicCatalog.Offline = false;
        }
    }

    [Fact]
    public void MusicPermissionsAreInIdentitysCatalog() =>
        Assert.Subset(IdentityPermission.All.ToHashSet(StringComparer.Ordinal), MusicPermission.All.ToHashSet(StringComparer.Ordinal));

    private static async Task<JsonElement> Anonymous(HttpClient client, string path)
    {
        using var response = await IdentityApi.CallAsync(client, HttpMethod.Get, path);
        return await TicketingApi.ReadAsync(response, HttpStatusCode.OK);
    }

    private static async Task<JsonElement> Request(HttpClient client, IdentityLogin fan, string path, object body, HttpStatusCode expected, string? errorKey = null)
    {
        var result = await TicketingApi.SendAsync(client, HttpMethod.Post, path, fan, body, expected);
        if (errorKey is not null)
        {
            Assert.Contains(errorKey, result.GetRawText(), StringComparison.Ordinal);
        }

        return result;
    }
}
