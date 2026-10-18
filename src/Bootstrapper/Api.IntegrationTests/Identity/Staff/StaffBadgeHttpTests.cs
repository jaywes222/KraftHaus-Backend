using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Api.IntegrationTests.Identity.Auditing;
using Api.IntegrationTests.Ticketing;
using Identity.Domain.Roles;
using Xunit;

namespace Api.IntegrationTests.Identity.Staff;

/// <summary>
///     Printable staff badges: issue, title, photo, the public check the QR opens, and the badge stopping to check out
///     when it is reissued or the person loses their staff role.
/// </summary>
[Collection(IdentityApiCollection.Name)]
public sealed class StaffBadgeHttpTests(IdentityAuditApiFixture fixture)
{
    private const string Staff = "/api/identity/staff";

    // The smallest valid PNG (1x1): enough for the server's content check.
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");

    private readonly TicketingApi _api = new(fixture);

    [Fact]
    public async Task ABadgeChecksOutWhileThePersonIsStaff()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.AdminAsync(client);
        var door = await _api.AdminAsync(client, IdentityRole.Support);
        var customer = await _api.CustomerAsync(client);
        var badgePath = $"{Staff}/users/{door.UserId}/badge";

        // Not issued yet: the view has no code.
        var fresh = await TicketingApi.SendAsync(client, HttpMethod.Get, badgePath, admin, null, HttpStatusCode.OK);
        Assert.Equal(JsonValueKind.Null, fresh.GetProperty("code").ValueKind);
        Assert.Equal("Support", fresh.GetProperty("roles")[0].GetProperty("role").GetString());

        // Support can see badges but not issue them; customers have none.
        await TicketingApi.SendAsync(client, HttpMethod.Post, badgePath, door, new { reissue = false }, HttpStatusCode.Forbidden);
        await TicketingApi.SendAsync(client, HttpMethod.Get, $"{Staff}/users/{customer.UserId}/badge", admin, null, HttpStatusCode.NotFound);

        var issued = await TicketingApi.SendAsync(client, HttpMethod.Post, badgePath, admin, new { reissue = false }, HttpStatusCode.OK);
        var code = issued.GetProperty("code").GetString()!;
        Assert.Equal(16, code.Length);

        await TicketingApi.SendAsync(client, HttpMethod.Put, badgePath, admin, new { title = "  Head of the door " }, HttpStatusCode.OK);

        // The member uploads their own photo; a file that isn't an image is refused.
        Assert.Equal(HttpStatusCode.BadRequest, await UploadAsync(client, door, "/api/identity/me/badge/photo", "not an image"u8.ToArray()));
        Assert.Equal(HttpStatusCode.OK, await UploadAsync(client, door, "/api/identity/me/badge/photo", Png));

        var mine = await TicketingApi.SendAsync(client, HttpMethod.Get, "/api/identity/me/badge", door, null, HttpStatusCode.OK);
        Assert.Equal("Head of the door", mine.GetProperty("title").GetString());
        var photoHref = mine.GetProperty("photoHref").GetString()!;
        using (var photo = await IdentityApi.CallAsync(client, HttpMethod.Get, photoHref))
        {
            Assert.Equal(HttpStatusCode.OK, photo.StatusCode);
            Assert.Equal("image/png", photo.Content.Headers.ContentType?.MediaType);
        }

        // Scanning the QR (anonymous): valid, with the name, title and role. A code typed with dashes still matches.
        var check = await AnonymousAsync(client, $"{Staff}/badges/{code[..8]}-{code[8..]}");
        Assert.True(check.GetProperty("valid").GetBoolean());
        Assert.Equal("Head of the door", check.GetProperty("title").GetString());
        Assert.Equal("Support", check.GetProperty("roles")[0].GetProperty("role").GetString());

        Assert.False((await AnonymousAsync(client, $"{Staff}/badges/ZZZZZZZZZZZZZZZZ")).GetProperty("valid").GetBoolean());

        // Reissued: the old printout stops checking out.
        var reissued = await TicketingApi.SendAsync(client, HttpMethod.Post, badgePath, admin, new { reissue = true }, HttpStatusCode.OK);
        var newCode = reissued.GetProperty("code").GetString()!;
        Assert.NotEqual(code, newCode);
        Assert.False((await AnonymousAsync(client, $"{Staff}/badges/{code}")).GetProperty("valid").GetBoolean());
        Assert.True((await AnonymousAsync(client, $"{Staff}/badges/{newCode}")).GetProperty("valid").GetBoolean());

        // The role goes: so does the badge.
        var roles = await TicketingApi.SendAsync(client, HttpMethod.Get, $"{Staff}/users/{door.UserId}/roles", admin, null, HttpStatusCode.OK);
        foreach (var role in roles.EnumerateArray())
        {
            using var revoked = await IdentityApi.CallAsync(client, HttpMethod.Post,
                $"{Staff}/roles/{role.GetProperty("assignmentId").GetGuid()}/revoke", null, admin.AccessToken);
            Assert.Equal(HttpStatusCode.NoContent, revoked.StatusCode);
        }

        var gone = await AnonymousAsync(client, $"{Staff}/badges/{newCode}");
        Assert.False(gone.GetProperty("valid").GetBoolean());
        Assert.Equal(JsonValueKind.Null, gone.GetProperty("displayName").ValueKind);
    }

    private static async Task<JsonElement> AnonymousAsync(HttpClient client, string path)
    {
        using var response = await IdentityApi.CallAsync(client, HttpMethod.Get, path);
        return await TicketingApi.ReadAsync(response, HttpStatusCode.OK);
    }

    private static async Task<HttpStatusCode> UploadAsync(HttpClient client, IdentityLogin caller, string path, byte[] bytes)
    {
        using var content = new MultipartFormDataContent();
        using var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(file, "file", "photo.png");

        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", caller.AccessToken);
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }
}
