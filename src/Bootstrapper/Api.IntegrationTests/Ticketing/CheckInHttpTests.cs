using System.Net;
using System.Text.Json;
using Api.IntegrationTests.Identity.Auditing;
using Identity.Domain.Roles;
using Xunit;

namespace Api.IntegrationTests.Ticketing;

/// <summary>UF-012 / UF-013 at the door: signed QR, the closed set of outcomes, double scans, manual check-in and search.</summary>
[Collection(IdentityApiCollection.Name)]
public sealed class CheckInHttpTests(IdentityAuditApiFixture fixture)
{
    private const string Ops = "/api/ops/check-in";

    private readonly TicketingApi _api = new(fixture);

    [Fact]
    public async Task Scan_AdmitsOnce_EvenWhenTwoScannersRace()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.AdminAsync(client);
        var sale = await TicketingApi.PublishedEventAsync(client, admin);
        var customer = await _api.CustomerAsync(client);
        var tickets = await _api.BuyAsync(client, customer, sale, 2);
        var door = await _api.AdminAsync(client, IdentityRole.Support);

        Assert.All(tickets, x => Assert.StartsWith("KH1.", x.GetProperty("qrPayload").GetString(), StringComparison.Ordinal));

        var first = await ScanAsync(client, door, sale.DateId, tickets[0].GetProperty("qrPayload").GetString());
        Assert.Equal("Admitted", first.GetProperty("outcome").GetString());
        Assert.Equal("Regular", first.GetProperty("tierName").GetString());

        var again = await ScanAsync(client, door, sale.DateId, tickets[0].GetProperty("qrPayload").GetString());
        Assert.Equal("AlreadyCheckedIn", again.GetProperty("outcome").GetString());
        Assert.NotEqual(JsonValueKind.Null, again.GetProperty("checkedInAt").ValueKind);

        // Two scanners, same ticket, same moment: exactly one admits.
        var second = await _api.AdminAsync(client, IdentityRole.Support);
        var payload = tickets[1].GetProperty("qrPayload").GetString();
        var race = await Task.WhenAll(ScanAsync(client, door, sale.DateId, payload), ScanAsync(client, second, sale.DateId, payload));
        Assert.Equal(["Admitted", "AlreadyCheckedIn"], race.Select(x => x.GetProperty("outcome").GetString()).Order(StringComparer.Ordinal));

        var stats = await TicketingApi.SendAsync(client, HttpMethod.Get, $"{Ops}/dates/{sale.DateId}/stats", door, null, HttpStatusCode.OK);
        Assert.Equal(2, stats.GetProperty("checkedIn").GetInt32());
        Assert.Equal(0, stats.GetProperty("notYetArrived").GetInt32());

        var mine = await TicketingApi.SendAsync(client, HttpMethod.Get, "/api/client/tickets", customer, null, HttpStatusCode.OK);
        Assert.All(mine.EnumerateArray().Where(x => x.GetProperty("eventId").GetGuid() == sale.EventId),
            x => Assert.Equal("CheckedIn", x.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task ForgedTamperedAndWrongDateScans_AreRejected()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.AdminAsync(client);
        var sale = await TicketingApi.PublishedEventAsync(client, admin);
        var customer = await _api.CustomerAsync(client);
        var ticket = (await _api.BuyAsync(client, customer, sale, 1))[0];
        var door = await _api.AdminAsync(client, IdentityRole.Support);
        var payload = ticket.GetProperty("qrPayload").GetString()!;

        var parts = payload.Split('.');
        var tampered = $"{parts[0]}.{parts[1]}.{parts[2]}.{(parts[3][0] == 'A' ? 'B' : 'A')}{parts[3][1..]}";

        Assert.Equal("Invalid", (await ScanAsync(client, door, sale.DateId, tampered)).GetProperty("outcome").GetString());
        Assert.Equal("Invalid", (await ScanAsync(client, door, sale.DateId, "https://evil.example/qr")).GetProperty("outcome").GetString());
        Assert.Equal("WrongDate", (await ScanAsync(client, door, Guid.NewGuid(), payload)).GetProperty("outcome").GetString());

        // Still admissible on the right date afterwards.
        Assert.Equal("Admitted", (await ScanAsync(client, door, sale.DateId, payload)).GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task ManualCheckIn_ByTypedCodeOrBySearch()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.AdminAsync(client);
        var sale = await TicketingApi.PublishedEventAsync(client, admin);
        var customer = await _api.CustomerAsync(client);
        var tickets = await _api.BuyAsync(client, customer, sale, 2);
        var door = await _api.AdminAsync(client, IdentityRole.Support);

        // Typed in lower case with a dash, as people do.
        var code = tickets[0].GetProperty("code").GetString()!;
#pragma warning disable CA1308 // Simulates what door staff type; not a normalisation.
        var typed = $"{code[..5].ToLowerInvariant()}-{code[5..]}";
#pragma warning restore CA1308
        var byCode = await TicketingApi.SendAsync(client, HttpMethod.Post, $"{Ops}/manual", door,
            new { eventDateId = sale.DateId, code = typed, ticketId = (Guid?)null }, HttpStatusCode.OK);
        Assert.Equal("Admitted", byCode.GetProperty("outcome").GetString());

        // Search by the last four digits of the phone, then admit the hit.
        var hits = await TicketingApi.SendAsync(client, HttpMethod.Get, $"{Ops}/dates/{sale.DateId}/search?q={customer.Phone[^4..]}", door, null, HttpStatusCode.OK);
        Assert.Equal(2, hits.GetArrayLength());
        Assert.DoesNotContain(customer.Phone[4..], hits.ToString(), StringComparison.Ordinal);

        var waiting = hits.EnumerateArray().Single(x => x.GetProperty("status").GetString() == "Valid");
        var byId = await TicketingApi.SendAsync(client, HttpMethod.Post, $"{Ops}/manual", door,
            new { eventDateId = sale.DateId, code = (string?)null, ticketId = waiting.GetProperty("ticketId").GetGuid() }, HttpStatusCode.OK);
        Assert.Equal("Admitted", byId.GetProperty("outcome").GetString());

        var unknown = await TicketingApi.SendAsync(client, HttpMethod.Post, $"{Ops}/manual", door,
            new { eventDateId = sale.DateId, code = "ZZZZZZZZZZ", ticketId = (Guid?)null }, HttpStatusCode.OK);
        Assert.Equal("Invalid", unknown.GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task CheckIn_NeedsTheCheckInPermission()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var customer = await _api.CustomerAsync(client);

        await TicketingApi.SendAsync(client, HttpMethod.Post, $"{Ops}/scan", customer, new { eventDateId = Guid.NewGuid(), payload = "x" }, HttpStatusCode.Forbidden);

        using var anonymous = await IdentityApi.CallAsync(client, HttpMethod.Post, $"{Ops}/scan", new { eventDateId = Guid.NewGuid(), payload = "x" });
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    private static Task<JsonElement> ScanAsync(HttpClient client, IdentityLogin staff, Guid dateId, string? payload) =>
        TicketingApi.SendAsync(client, HttpMethod.Post, $"{Ops}/scan", staff, new { eventDateId = dateId, payload }, HttpStatusCode.OK);
}
