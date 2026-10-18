using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Api.IntegrationTests.Commerce;
using Api.IntegrationTests.Identity.Auditing;
using Api.IntegrationTests.Ticketing;
using Xunit;

namespace Api.IntegrationTests.Displays;

/// <summary>Pairing and device-call helpers shared by the Displays HTTP tests.</summary>
internal sealed class DisplaysApi(IdentityAuditApiFixture fixture)
{
    public CommerceApi Commerce { get; } = new(fixture);

    public TicketingApi Ticketing => Commerce.Ticketing;

    /// <summary>A 1x1 transparent PNG.</summary>
    public static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

    /// <summary>Screen asks for a code, staff pair it, screen picks up its token.</summary>
    public static async Task<(Guid DeviceId, string Token)> PairAsync(
        HttpClient client,
        IdentityLogin admin,
        Guid eventId,
        Guid? defaultConfigurationId = null,
        string name = "Main bar TV")
    {
        var start = await AnonymousAsync(client, HttpMethod.Post, "/api/display/pairings", null, HttpStatusCode.Created);
        var pairingId = start.GetProperty("pairingId").GetGuid();
        var secret = start.GetProperty("secret").GetString()!;

        await TicketingApi.SendAsync(client, HttpMethod.Post, "/api/admin/displays/pair", admin,
            new { code = start.GetProperty("code").GetString(), name, location = "By the bar", eventId, defaultConfigurationId },
            HttpStatusCode.Created);

        var poll = await AnonymousAsync(client, HttpMethod.Post, $"/api/display/pairings/{pairingId}/poll", new { secret }, HttpStatusCode.OK);
        Assert.Equal("Paired", poll.GetProperty("status").GetString());

        return (poll.GetProperty("deviceId").GetGuid(), poll.GetProperty("deviceToken").GetString()!);
    }

    public static async Task<JsonElement> AnonymousAsync(HttpClient client, HttpMethod method, string path, object? body, HttpStatusCode expected)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await client.SendAsync(request);

        return await TicketingApi.ReadAsync(response, expected);
    }

    public static async Task<HttpResponseMessage> DeviceAsync(HttpClient client, string path, string? token, string scheme = "Device")
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue(scheme, token);
        }

        return await client.SendAsync(request);
    }

    public static async Task<JsonElement> FeedAsync(HttpClient client, string token)
    {
        using var response = await DeviceAsync(client, "/api/display/feed", token);

        return await TicketingApi.ReadAsync(response, HttpStatusCode.OK);
    }

    public static async Task<Guid> UploadPngAsync(HttpClient client, IdentityLogin admin, string fileName = "sponsor.png")
    {
        using var content = new MultipartFormDataContent();
        using var file = new ByteArrayContent(Png);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(file, "file", fileName);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/display-assets") { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        using var response = await client.SendAsync(request);

        return (await TicketingApi.ReadAsync(response, HttpStatusCode.Created)).GetProperty("id").GetGuid();
    }
}
