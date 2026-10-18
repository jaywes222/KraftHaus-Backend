using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Api.IntegrationTests.Identity.Auditing;
using Api.IntegrationTests.Ticketing;
using Content.Domain;
using Identity.Domain.Authorization;
using Identity.Domain.Roles;
using Xunit;

namespace Api.IntegrationTests.Content;

/// <summary>UF-024: articles from draft to published to taken down, covers, and the Web Studio's draft/publish blocks.</summary>
[Collection(IdentityApiCollection.Name)]
public sealed class ContentHttpTests(IdentityAuditApiFixture fixture)
{
    // The smallest valid PNG (1×1).
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    private static readonly int[] NotAnObject = [1, 2];

    private readonly TicketingApi _api = new(fixture);

    [Fact]
    public async Task ArticlesArePublishedTakenDownAndReadByVisitors()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.AdminAsync(client);
        var support = await _api.AdminAsync(client, IdentityRole.Support);
        var slug = $"lineup-{Guid.NewGuid():N}"[..20];

        var body = new { slug, title = "The line-up", summary = "Who's playing.", body = "## DJs\n\nDJ Kace", authorName = "KRAFTHAUS", tags = new[] { "Music", "Event-001" } };
        await TicketingApi.SendAsync(client, HttpMethod.Post, "/api/admin/articles", support, body, HttpStatusCode.Forbidden);

        var created = await TicketingApi.SendAsync(client, HttpMethod.Post, "/api/admin/articles", admin, body, HttpStatusCode.Created);
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal("Draft", created.GetProperty("status").GetString());
        await TicketingApi.SendAsync(client, HttpMethod.Post, "/api/admin/articles", admin, body, HttpStatusCode.Conflict);

        // Drafts are invisible to visitors.
        using (var hidden = await IdentityApi.CallAsync(client, HttpMethod.Get, $"/api/client/articles/{slug}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        }

        // A cover: the stored file is served publicly; a non-image is refused.
        Assert.Equal(HttpStatusCode.BadRequest, (await UploadAsync(client, admin, id, [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13])).StatusCode);
        using var uploaded = await UploadAsync(client, admin, id, Png);
        Assert.Equal(HttpStatusCode.OK, uploaded.StatusCode);
        var cover = JsonSerializer.Deserialize<string>(await uploaded.Content.ReadAsStringAsync())!;
        using (var image = await client.GetAsync(new Uri(cover, UriKind.Relative)))
        {
            Assert.Equal(HttpStatusCode.OK, image.StatusCode);
            Assert.Equal("image/png", image.Content.Headers.ContentType?.MediaType);
        }

        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/articles/{id}/publish", admin, null, HttpStatusCode.OK);

        var read = await Anonymous(client, $"/api/client/articles/{slug}");
        Assert.Equal("## DJs\n\nDJ Kace", read.GetProperty("body").GetString());
        Assert.Equal(cover, read.GetProperty("coverHref").GetString());

        var byTag = await Anonymous(client, "/api/client/articles?tag=event-001");
        Assert.Contains(byTag.EnumerateArray(), x => x.GetProperty("slug").GetString() == slug);
        var otherTag = await Anonymous(client, "/api/client/articles?tag=food");
        Assert.DoesNotContain(otherTag.EnumerateArray(), x => x.GetProperty("slug").GetString() == slug);

        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/articles/{id}/unpublish", admin, null, HttpStatusCode.OK);
        using (var gone = await IdentityApi.CallAsync(client, HttpMethod.Get, $"/api/client/articles/{slug}"))
        {
            Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        }

        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/articles/{id}/archive", admin, null, HttpStatusCode.OK);
        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/articles/{id}/publish", admin, null, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task TheWebStudioPublishesDraftsVisitorsThenSee()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.AdminAsync(client);

        var blocks = await TicketingApi.SendAsync(client, HttpMethod.Get, "/api/admin/site/blocks", admin, null, HttpStatusCode.OK);
        Assert.Equal(Enum.GetValues<SiteBlockKey>().Length, blocks.GetArrayLength());

        var headline = $"Made Together {Guid.NewGuid():N}";
        await TicketingApi.SendAsync(client, HttpMethod.Put, "/api/admin/site/blocks/Hero", admin, new { content = NotAnObject }, HttpStatusCode.BadRequest);
        var draft = await TicketingApi.SendAsync(client, HttpMethod.Put, "/api/admin/site/blocks/Hero", admin,
            new { content = new { headline, cta = "Get tickets" } }, HttpStatusCode.OK);
        Assert.True(draft.GetProperty("hasUnpublishedChanges").GetBoolean());

        // Not live until published.
        var before = await Anonymous(client, "/api/client/site");
        Assert.False(before.TryGetProperty("hero", out var oldHero) && oldHero.GetProperty("headline").GetString() == headline);

        await TicketingApi.SendAsync(client, HttpMethod.Put, "/api/admin/site/blocks/Announcement", admin,
            new { content = new { text = "Tickets on sale Monday" } }, HttpStatusCode.OK);
        var published = await TicketingApi.SendAsync(client, HttpMethod.Post, "/api/admin/site/publish", admin, null, HttpStatusCode.OK);
        Assert.True(published.GetArrayLength() >= 2);

        var site = await Anonymous(client, "/api/client/site");
        Assert.Equal(headline, site.GetProperty("hero").GetProperty("headline").GetString());
        Assert.Equal("Tickets on sale Monday", site.GetProperty("announcement").GetProperty("text").GetString());

        await TicketingApi.SendAsync(client, HttpMethod.Post, "/api/admin/site/blocks/Hero/publish", admin, null, HttpStatusCode.Conflict);
    }

    [Fact]
    public void ContentPermissionsAreInIdentitysCatalog() =>
        Assert.Subset(IdentityPermission.All.ToHashSet(StringComparer.Ordinal), ContentPermission.All.ToHashSet(StringComparer.Ordinal));

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, IdentityLogin admin, Guid articleId, byte[] bytes)
    {
        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        form.Add(file, "file", "cover.png");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/articles/{articleId}/cover") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> Anonymous(HttpClient client, string path)
    {
        using var response = await IdentityApi.CallAsync(client, HttpMethod.Get, path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }
}
