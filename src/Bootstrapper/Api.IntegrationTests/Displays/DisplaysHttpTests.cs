using System.Net;
using Api.IntegrationTests.Commerce;
using Api.IntegrationTests.Identity.Auditing;
using Api.IntegrationTests.Ticketing;
using Displays.Domain.Common;
using Identity.Domain.Authorization;
using Identity.Domain.Roles;
using Payments.Domain.Payments;
using Xunit;

namespace Api.IntegrationTests.Displays;

/// <summary>
///     Screens end to end (UF-019): pairing with a code and a one-time token, device-token auth and revocation, slides
///     resolved from the programme, Commerce menus and ready orders, images, and time-scheduled switching.
/// </summary>
[Collection(IdentityApiCollection.Name)]
public sealed class DisplaysHttpTests(IdentityAuditApiFixture fixture)
{
    private readonly DisplaysApi _api = new(fixture);

    [Fact]
    public async Task AScreenPairsWithACodeAndGetsItsTokenOnce()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.Ticketing.AdminAsync(client);
        var support = await _api.Ticketing.AdminAsync(client, IdentityRole.Support);
        var customer = await _api.Ticketing.CustomerAsync(client);
        var sale = await TicketingApi.PublishedEventAsync(client, admin);

        var start = await DisplaysApi.AnonymousAsync(client, HttpMethod.Post, "/api/display/pairings", null, HttpStatusCode.Created);
        var pairingId = start.GetProperty("pairingId").GetGuid();
        var code = start.GetProperty("code").GetString()!;
        var secret = start.GetProperty("secret").GetString()!;
        Assert.Equal(6, code.Length);

        // Polling needs the screen's secret; the code alone gets nobody a token.
        await DisplaysApi.AnonymousAsync(client, HttpMethod.Post, $"/api/display/pairings/{pairingId}/poll", new { secret = "guess" }, HttpStatusCode.NotFound);
        var waiting = await DisplaysApi.AnonymousAsync(client, HttpMethod.Post, $"/api/display/pairings/{pairingId}/poll", new { secret }, HttpStatusCode.OK);
        Assert.Equal("Waiting", waiting.GetProperty("status").GetString());

        var pair = new { code = " " + code.Insert(3, "-") + " ", name = "Main bar TV", location = "By the bar", eventId = sale.EventId, defaultConfigurationId = (Guid?)null };
        await TicketingApi.SendAsync(client, HttpMethod.Post, "/api/admin/displays/pair", support, pair, HttpStatusCode.Forbidden);
        await TicketingApi.SendAsync(client, HttpMethod.Post, "/api/admin/displays/pair", customer, pair, HttpStatusCode.Forbidden);
        var wrong = await TicketingApi.SendAsync(client, HttpMethod.Post, "/api/admin/displays/pair", admin, pair with { code = "ZZZZZZ" }, HttpStatusCode.NotFound);
        Assert.Equal("Displays.PairingCodeNotFound", wrong.GetProperty("errorKey").GetString());

        var device = await TicketingApi.SendAsync(client, HttpMethod.Post, "/api/admin/displays/pair", admin, pair, HttpStatusCode.Created);
        var deviceId = device.GetProperty("id").GetGuid();
        Assert.Equal("Active", device.GetProperty("status").GetString());

        // The code is used up.
        await TicketingApi.SendAsync(client, HttpMethod.Post, "/api/admin/displays/pair", admin, pair, HttpStatusCode.NotFound);

        var paired = await DisplaysApi.AnonymousAsync(client, HttpMethod.Post, $"/api/display/pairings/{pairingId}/poll", new { secret }, HttpStatusCode.OK);
        Assert.Equal("Paired", paired.GetProperty("status").GetString());
        Assert.Equal(deviceId, paired.GetProperty("deviceId").GetGuid());
        var token = paired.GetProperty("deviceToken").GetString()!;
        Assert.StartsWith("khd_", token, StringComparison.Ordinal);

        var again = await DisplaysApi.AnonymousAsync(client, HttpMethod.Post, $"/api/display/pairings/{pairingId}/poll", new { secret }, HttpStatusCode.OK);
        Assert.Equal("Delivered", again.GetProperty("status").GetString());
        Assert.Equal(System.Text.Json.JsonValueKind.Null, again.GetProperty("deviceToken").ValueKind);

        // Nothing configured yet: the screen shows "no content", and is online.
        var feed = await DisplaysApi.FeedAsync(client, token);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, feed.GetProperty("configurationId").ValueKind);
        Assert.Empty(feed.GetProperty("slides").EnumerateArray());
        Assert.Equal("KraftHaus 001", feed.GetProperty("event").GetProperty("title").GetString());

        var listed = await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/admin/displays?eventId={sale.EventId}", support, null, HttpStatusCode.OK);
        Assert.True(listed.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == deviceId).GetProperty("online").GetBoolean());

        // Device tokens only work on screen routes, and staff tokens never work there.
        using (var noToken = await DisplaysApi.DeviceAsync(client, "/api/display/feed", null))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, noToken.StatusCode);
        }

        using (var staffToken = await DisplaysApi.DeviceAsync(client, "/api/display/feed", admin.AccessToken, "Bearer"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, staffToken.StatusCode);
        }

        using (var deviceOnAdmin = await DisplaysApi.DeviceAsync(client, "/api/admin/displays", token, "Bearer"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, deviceOnAdmin.StatusCode);
        }

        // Revoking stops the token at once.
        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/displays/{deviceId}/revoke", admin, new { reason = "" }, HttpStatusCode.BadRequest);
        var revoked = await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/displays/{deviceId}/revoke", admin, new { reason = "Screen stolen" }, HttpStatusCode.OK);
        Assert.Equal("Revoked", revoked.GetProperty("status").GetString());
        using (var afterRevoke = await DisplaysApi.DeviceAsync(client, "/api/display/feed", token))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, afterRevoke.StatusCode);
        }
    }

    [Fact]
    public async Task SlidesShowTheProgrammeMenuReadyOrdersAndImages()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.Ticketing.AdminAsync(client);
        var finance = await _api.Ticketing.AdminAsync(client, IdentityRole.Finance);
        var sale = await TicketingApi.PublishedEventAsync(client, admin, startsIn: TimeSpan.FromMinutes(30));
        var grill = await _api.Commerce.SellingKraftAsync(client, admin, finance, "Screen Grill", sale.EventId);
        var juice = await _api.Commerce.SellingKraftAsync(client, admin, finance, "Screen Juice", sale.EventId);
        var nyama = await CommerceApi.MenuItemAsync(client, admin, grill, sale.EventId, "Nyama choma", 800m);
        await CommerceApi.MenuItemAsync(client, admin, juice, sale.EventId, "Passion juice", 200m);

        var now = fixture.Clock.GetUtcNow();
        await AddSlotAsync(client, admin, sale.EventId, "Opening set", now.AddMinutes(-10), now.AddMinutes(20));
        await AddSlotAsync(client, admin, sale.EventId, "Panel: Made Together", now.AddMinutes(30), now.AddMinutes(60));
        await AddSlotAsync(client, admin, sale.EventId, "Headline", now.AddMinutes(90), now.AddMinutes(150));
        await AddSlotAsync(client, admin, sale.EventId, "Already over", now.AddMinutes(-90), now.AddMinutes(-30));
        var badSlot = await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/events/{sale.EventId}/programme", admin,
            new { eventDateId = Guid.CreateVersion7(), title = "Wrong date", startsAt = now, endsAt = now.AddHours(1) }, HttpStatusCode.BadRequest);
        Assert.Equal("Displays.SlotInvalid", badSlot.GetProperty("errorKey").GetString());

        var publicProgramme = await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/client/events/{sale.Slug}/programme", admin, null, HttpStatusCode.OK);
        Assert.Equal(4, publicProgramme.GetArrayLength());

        var logo = await DisplaysApi.UploadPngAsync(client, admin);
        using (var image = await client.GetAsync(new Uri($"/api/public/display-assets/{logo}", UriKind.Relative)))
        {
            Assert.Equal(HttpStatusCode.OK, image.StatusCode);
            Assert.Equal("image/png", image.Content.Headers.ContentType!.MediaType);
            Assert.Equal(DisplaysApi.Png, await image.Content.ReadAsByteArrayAsync());
        }

        var configuration = await TicketingApi.SendAsync(client, HttpMethod.Post, "/api/admin/display-configurations", admin, new
        {
            eventId = sale.EventId,
            name = "Main bar loop",
            slides = new object[]
            {
                new { kind = "Schedule", durationSeconds = 15 },
                new { kind = "Menu", durationSeconds = 20, title = "Grill", kraftIds = new[] { grill } },
                new { kind = "OrdersReady", durationSeconds = 15 },
                new { kind = "Announcement", durationSeconds = 10, title = "Made Together.", body = "Doors close at 23:00.", assetId = logo },
                new { kind = "Sponsor", durationSeconds = 8, assetId = logo }
            }
        }, HttpStatusCode.Created);
        var configurationId = configuration.GetProperty("id").GetGuid();

        var (deviceId, token) = await DisplaysApi.PairAsync(client, admin, sale.EventId, configurationId);

        var feed = await DisplaysApi.FeedAsync(client, token);
        Assert.Equal(configurationId, feed.GetProperty("configurationId").GetGuid());
        var slides = feed.GetProperty("slides").EnumerateArray().ToList();
        Assert.Equal(["Schedule", "Menu", "OrdersReady", "Announcement", "Sponsor"], slides.Select(x => x.GetProperty("kind").GetString()));

        var schedule = slides[0].GetProperty("schedule");
        Assert.Equal(["Opening set"], schedule.GetProperty("now").EnumerateArray().Select(x => x.GetProperty("title").GetString()));
        Assert.Equal(["Panel: Made Together", "Headline"], schedule.GetProperty("next").EnumerateArray().Select(x => x.GetProperty("title").GetString()));

        var menu = slides[1].GetProperty("menu").EnumerateArray().ToList();
        Assert.Equal("Screen Grill", Assert.Single(menu).GetProperty("name").GetString());
        Assert.Equal(nyama, menu[0].GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.Empty(slides[2].GetProperty("readyOrders").EnumerateArray());
        Assert.Equal($"/api/public/display-assets/{logo}", slides[3].GetProperty("imageUrl").GetString());

        // A paid order marked ready shows up by number and stall only.
        var buyer = await _api.Ticketing.CustomerAsync(client);
        await _api.Ticketing.BuyAsync(client, buyer, sale, 1);
        System.Text.Json.JsonElement checkout;
        using (var placed = await CommerceApi.CheckoutAsync(client, buyer,
                   new { eventSlug = sale.Slug, kind = "Live", items = new[] { new { menuItemId = nyama, quantity = 1 } }, ageConfirmed = false, payerPhone = (string?)null },
                   Guid.NewGuid().ToString("N")))
        {
            checkout = await TicketingApi.ReadAsync(placed, HttpStatusCode.Created);
        }

        var orderId = checkout.GetProperty("orders")[0].GetProperty("id").GetGuid();
        var paid = await _api.Commerce.PayAsync(client, buyer, orderId);
        var staff = await _api.Commerce.KraftStaffAsync(client, grill, IdentityRole.KraftStaff);
        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/ops/krafts/{grill}/orders/{orderId}/accept", staff, null, HttpStatusCode.OK);
        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/ops/krafts/{grill}/orders/{orderId}/ready", staff, null, HttpStatusCode.OK);

        feed = await DisplaysApi.FeedAsync(client, token);
        var ready = Assert.Single(feed.GetProperty("slides")[2].GetProperty("readyOrders").EnumerateArray());
        Assert.Equal(paid.GetProperty("reference").GetString(), ready.GetProperty("reference").GetString());
        Assert.Equal("Screen Grill", ready.GetProperty("kraftName").GetString());
        Assert.DoesNotContain(buyer.Phone[^4..], feed.GetRawText(), StringComparison.Ordinal);

        // What screens show cannot be pulled out from under them.
        var inUse = await TicketingApi.SendAsync(client, HttpMethod.Delete, $"/api/admin/display-configurations/{configurationId}", admin, null, HttpStatusCode.Conflict);
        Assert.Equal("Displays.ConfigurationInUse", inUse.GetProperty("errorKey").GetString());
        var imageInUse = await TicketingApi.SendAsync(client, HttpMethod.Delete, $"/api/admin/display-assets/{logo}", admin, null, HttpStatusCode.Conflict);
        Assert.Equal("Displays.AssetInUse", imageInUse.GetProperty("errorKey").GetString());

        // Unknown images and Krafts are refused instead of leaving blank slides.
        var unknown = await TicketingApi.SendAsync(client, HttpMethod.Put, $"/api/admin/display-configurations/{configurationId}", admin, new
        {
            name = "Main bar loop",
            slides = new object[] { new { kind = "Sponsor", durationSeconds = 8, assetId = Guid.CreateVersion7() }, new { kind = "Menu", durationSeconds = 8, kraftIds = new[] { Guid.CreateVersion7() } } }
        }, HttpStatusCode.BadRequest);
        Assert.Equal(2, unknown.GetProperty("errors").GetArrayLength());

        var device = await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/admin/displays/{deviceId}", admin, null, HttpStatusCode.OK);
        Assert.Equal(configurationId, device.GetProperty("showingConfigurationId").GetGuid());
    }

    [Fact]
    public async Task AScheduleSwitchesWhatAScreenShows()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.Ticketing.AdminAsync(client);
        var sale = await TicketingApi.PublishedEventAsync(client, admin);
        var other = await TicketingApi.PublishedEventAsync(client, admin);
        var daytime = await CreateAnnouncementAsync(client, admin, sale.EventId, "Daytime");
        var evening = await CreateAnnouncementAsync(client, admin, sale.EventId, "Evening");
        var elsewhere = await CreateAnnouncementAsync(client, admin, other.EventId, "Elsewhere");
        var (deviceId, token) = await DisplaysApi.PairAsync(client, admin, sale.EventId, daytime);

        var now = fixture.Clock.GetUtcNow();
        var notMine = await TicketingApi.SendAsync(client, HttpMethod.Put, $"/api/admin/displays/{deviceId}/schedule", admin,
            new { entries = new[] { new { configurationId = elsewhere, startsAt = now, endsAt = now.AddHours(1) } } }, HttpStatusCode.BadRequest);
        Assert.Equal("Displays.ScheduleInvalid", notMine.GetProperty("errorKey").GetString());

        await TicketingApi.SendAsync(client, HttpMethod.Put, $"/api/admin/displays/{deviceId}/schedule", admin,
            new { entries = new[] { new { configurationId = evening, startsAt = now.AddMinutes(-1), endsAt = now.AddHours(2) } } }, HttpStatusCode.OK);

        var feed = await DisplaysApi.FeedAsync(client, token);
        Assert.Equal(evening, feed.GetProperty("configurationId").GetGuid());
        Assert.Equal("Evening", feed.GetProperty("slides")[0].GetProperty("title").GetString());
        Assert.True(feed.GetProperty("nextChangeAt").GetDateTimeOffset() > now.AddHours(1));

        await TicketingApi.SendAsync(client, HttpMethod.Put, $"/api/admin/displays/{deviceId}/schedule", admin, new { entries = Array.Empty<object>() }, HttpStatusCode.OK);
        feed = await DisplaysApi.FeedAsync(client, token);
        Assert.Equal(daytime, feed.GetProperty("configurationId").GetGuid());
    }

    [Fact]
    public async Task TheDisplayHubAdmitsScreensOnly()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.Ticketing.AdminAsync(client);
        var sale = await TicketingApi.PublishedEventAsync(client, admin);
        var (_, token) = await DisplaysApi.PairAsync(client, admin, sale.EventId);

        async Task<HttpStatusCode> NegotiateAsync(string? queryToken)
        {
            var path = "/hubs/display/negotiate?negotiateVersion=1" + (queryToken is null ? string.Empty : $"&access_token={queryToken}");
            using var response = await client.PostAsync(new Uri(path, UriKind.Relative), null);
            return response.StatusCode;
        }

        Assert.Equal(HttpStatusCode.OK, await NegotiateAsync(token));
        Assert.Equal(HttpStatusCode.Unauthorized, await NegotiateAsync(null));
        Assert.Equal(HttpStatusCode.Unauthorized, await NegotiateAsync(admin.AccessToken));
    }

    [Fact]
    public async Task UploadsMustBeRealImages()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.Ticketing.AdminAsync(client);

        using var content = new MultipartFormDataContent();
        using var svg = new ByteArrayContent("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>"u8.ToArray());
        svg.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        content.Add(svg, "file", "logo.png");
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/display-assets") { Content = content };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", admin.AccessToken);
        using var response = await client.SendAsync(request);

        var error = await TicketingApi.ReadAsync(response, HttpStatusCode.BadRequest);
        Assert.Equal("Displays.AssetInvalid", error.GetProperty("errorKey").GetString());

        var unused = await DisplaysApi.UploadPngAsync(client, admin);
        await TicketingApi.SendAsync(client, HttpMethod.Delete, $"/api/admin/display-assets/{unused}", admin, null, HttpStatusCode.OK);
        using var gone = await client.GetAsync(new Uri($"/api/public/display-assets/{unused}", UriKind.Relative));
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
    }

    [Fact]
    public void DisplaysPermissionsAreInIdentitysCatalog() =>
        Assert.Subset(IdentityPermission.All.ToHashSet(StringComparer.Ordinal), DisplaysPermission.All.ToHashSet(StringComparer.Ordinal));

    private static Task<System.Text.Json.JsonElement> AddSlotAsync(HttpClient client, IdentityLogin admin, Guid eventId, string title, DateTimeOffset startsAt, DateTimeOffset endsAt) =>
        TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/events/{eventId}/programme", admin,
            new { title, who = "KraftHaus", stage = "Courtyard", startsAt, endsAt }, HttpStatusCode.Created);

    private static async Task<Guid> CreateAnnouncementAsync(HttpClient client, IdentityLogin admin, Guid eventId, string title)
    {
        var created = await TicketingApi.SendAsync(client, HttpMethod.Post, "/api/admin/display-configurations", admin, new
        {
            eventId,
            name = title + " loop",
            slides = new object[] { new { kind = "Announcement", durationSeconds = 10, title } }
        }, HttpStatusCode.Created);

        return created.GetProperty("id").GetGuid();
    }
}
