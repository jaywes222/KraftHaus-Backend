using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Api.IntegrationTests.Displays;
using Api.IntegrationTests.Identity.Auditing;
using Api.IntegrationTests.Ticketing;
using Identity.Domain.Roles;
using Xunit;

namespace Api.IntegrationTests.Commerce;

/// <summary>
///     Kraft extras: logos and the members wall, menu photos and add-ons, bookable activities, paying into the
///     platform's till instead of the Kraft's own, statements and settlements, the gallery and the DJ booth slide.
/// </summary>
[Collection(IdentityApiCollection.Name)]
public sealed class KraftExtrasHttpTests(IdentityAuditApiFixture fixture)
{
    private readonly CommerceApi _api = new(fixture);

    [Fact]
    public async Task APlatformTillKraftSellsAddOnsAndActivitiesAndIsSettled()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.Ticketing.AdminAsync(client);
        var finance = await _api.Ticketing.AdminAsync(client, IdentityRole.Finance);
        var sale = await TicketingApi.PublishedEventAsync(client, admin, startsIn: TimeSpan.FromMinutes(30));

        // No till of its own: money goes to the KRAFTHAUS till.
        var kraftId = await CommerceApi.CreateKraftAsync(client, admin, "Lakeside Rides", sale.EventId, commission: 10m);
        var terms = await TicketingApi.SendAsync(client, HttpMethod.Put, $"/api/admin/krafts/{kraftId}/events/{sale.EventId}", admin,
            new { commissionPercent = 10m, stall = "Jetty", paymentRouting = "PlatformTill" }, HttpStatusCode.OK);
        Assert.Equal("PlatformTill", terms.GetProperty("paymentRouting").GetString());

        Assert.Equal(HttpStatusCode.OK, await UploadAsync(client, admin, $"/api/admin/krafts/{kraftId}/logo", DisplaysApi.Png));
        Assert.Equal(HttpStatusCode.BadRequest, await UploadAsync(client, admin, $"/api/admin/krafts/{kraftId}/logo", "<svg/>"u8.ToArray()));
        using (var members = await IdentityApi.CallAsync(client, HttpMethod.Get, "/api/client/krafts"))
        {
            var list = await TicketingApi.ReadAsync(members, HttpStatusCode.OK);
            var me = list.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == kraftId);
            var logo = me.GetProperty("logoHref").GetString()!;
            using var image = await client.GetAsync(new Uri(logo, UriKind.Relative));
            Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        }

        var chips = await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/krafts/{kraftId}/menu", admin,
            new
            {
                eventId = sale.EventId, name = "Chips masala", description = (string?)null, category = "Food", price = 300m,
                liveStock = (int?)null, reservationAllocation = (int?)null, ageRestricted = (bool?)null, sortOrder = (int?)null,
                addOns = new[] { new { id = (Guid?)null, name = "Extra cheese", price = 50m }, new { id = (Guid?)null, name = "No chilli", price = 0m } }
            }, HttpStatusCode.Created);
        var chipsId = chips.GetProperty("id").GetGuid();
        var cheese = chips.GetProperty("addOns").EnumerateArray().Single(x => x.GetProperty("name").GetString() == "Extra cheese").GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, await UploadAsync(client, admin, $"/api/admin/krafts/{kraftId}/menu/{chipsId}/image", DisplaysApi.Png));

        // An activity needs its start time.
        var slot = fixture.Clock.GetUtcNow().AddHours(2);
        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/krafts/{kraftId}/menu", admin,
            new { eventId = sale.EventId, name = "Boat ride", category = "Activity", price = 1000m, liveStock = 8 }, HttpStatusCode.BadRequest);
        var boat = await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/krafts/{kraftId}/menu", admin,
            new { eventId = sale.EventId, name = "Boat ride", category = "Activity", price = 1000m, liveStock = 8, startsAt = slot, durationMinutes = 30 },
            HttpStatusCode.Created);
        var boatId = boat.GetProperty("id").GetGuid();

        var menu = await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/client/events/{sale.Slug}/menu", admin, null, HttpStatusCode.OK);
        var kraft = menu.GetProperty("krafts").EnumerateArray().Single(x => x.GetProperty("kraftId").GetGuid() == kraftId);
        Assert.NotNull(kraft.GetProperty("logoHref").GetString());
        var chipsItem = kraft.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == chipsId);
        Assert.Equal(2, chipsItem.GetProperty("addOns").GetArrayLength());
        Assert.NotNull(chipsItem.GetProperty("imageHref").GetString());

        var buyer = await _api.Ticketing.CustomerAsync(client);
        await _api.Ticketing.BuyAsync(client, buyer, sale, 1);

        // Food with an add-on: 2 x (300 + 50) + KES 30 service fee.
        JsonElement food;
        using (var placed = await CommerceApi.CheckoutAsync(client, buyer, new
               {
                   eventSlug = sale.Slug, kind = "Live",
                   items = new[] { new { menuItemId = chipsId, quantity = 2, addOnIds = new[] { cheese } } },
                   ageConfirmed = false, payerPhone = (string?)null
               }, Guid.NewGuid().ToString("N")))
        {
            food = await TicketingApi.ReadAsync(placed, HttpStatusCode.Created);
        }

        var foodOrder = food.GetProperty("orders")[0];
        Assert.Equal(730m, foodOrder.GetProperty("total").GetDecimal());
        Assert.Equal("Extra cheese", foodOrder.GetProperty("lines")[0].GetProperty("addOns").GetString());

        // An activity booking carries no service fee.
        JsonElement ride;
        using (var placed = await CommerceApi.CheckoutAsync(client, buyer, new
               {
                   eventSlug = sale.Slug, kind = "Live", items = new[] { new { menuItemId = boatId, quantity = 2 } },
                   ageConfirmed = false, payerPhone = (string?)null
               }, Guid.NewGuid().ToString("N")))
        {
            ride = await TicketingApi.ReadAsync(placed, HttpStatusCode.Created);
        }

        Assert.Equal(2000m, ride.GetProperty("orders")[0].GetProperty("total").GetDecimal());

        await _api.PayAsync(client, buyer, foodOrder.GetProperty("id").GetGuid());
        Assert.Null(fixture.PaymentProvider.PromptsTo(buyer.Phone)[^1].Request.ChannelId);
        await _api.PayAsync(client, buyer, ride.GetProperty("orders")[0].GetProperty("id").GetGuid());

        // Platform holds 2,730; the Kraft's share is 90% of the 2,700 subtotal = 2,430.
        var statements = await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/admin/events/{sale.EventId}/statements", finance, null, HttpStatusCode.OK);
        var statement = statements.EnumerateArray().Single(x => x.GetProperty("kraftId").GetGuid() == kraftId);
        Assert.Equal(2730m, statement.GetProperty("collectedInPlatformTill").GetDecimal());
        Assert.Equal(2430m, statement.GetProperty("platformOwesKraft").GetDecimal());
        Assert.Equal(30m, statement.GetProperty("serviceFees").GetDecimal());
        Assert.Equal(270m, statement.GetProperty("commission").GetDecimal());
        Assert.Equal(2430m, statement.GetProperty("balance").GetDecimal());

        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/krafts/{kraftId}/settlements", finance,
            new { eventId = sale.EventId, direction = "PlatformPaidKraft", amount = 2000m, reference = "", note = (string?)null, paidAt = (DateTimeOffset?)null },
            HttpStatusCode.BadRequest);
        var settled = await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/krafts/{kraftId}/settlements", finance,
            new { eventId = sale.EventId, direction = "PlatformPaidKraft", amount = 2000m, reference = "QK7PAYOUT1", note = "First payout", paidAt = (DateTimeOffset?)null },
            HttpStatusCode.Created);
        Assert.Equal(430m, settled.GetProperty("balance").GetDecimal());
        Assert.Single(settled.GetProperty("settlements").EnumerateArray());
    }

    [Fact]
    public async Task TheGalleryAndTheDjBoothSlide()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.Ticketing.AdminAsync(client);
        var sale = await TicketingApi.PublishedEventAsync(client, admin);
        var fan = await _api.Ticketing.CustomerAsync(client);
        await _api.Ticketing.BuyAsync(client, fan, sale, 1);

        var asset = await DisplaysApi.UploadPngAsync(client, admin, "night.png");
        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/events/{sale.EventId}/gallery", admin,
            new { assetId = Guid.NewGuid(), caption = "Nope", sortOrder = (int?)null }, HttpStatusCode.NotFound);
        var photo = await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/events/{sale.EventId}/gallery", admin,
            new { assetId = asset, caption = "Golden hour on the terrace", sortOrder = (int?)null }, HttpStatusCode.Created);

        using (var gallery = await IdentityApi.CallAsync(client, HttpMethod.Get, "/api/client/gallery"))
        {
            var list = await TicketingApi.ReadAsync(gallery, HttpStatusCode.OK);
            var ours = list.EnumerateArray().Single(x => x.GetProperty("eventId").GetGuid() == sale.EventId);
            Assert.Equal("Golden hour on the terrace", Assert.Single(ours.GetProperty("photos").EnumerateArray()).GetProperty("caption").GetString());
        }

        // The image can't be deleted while the gallery shows it.
        await TicketingApi.SendAsync(client, HttpMethod.Delete, $"/api/admin/display-assets/{asset}", admin, null, HttpStatusCode.Conflict);
        await TicketingApi.SendAsync(client, HttpMethod.Delete, $"/api/admin/gallery/{photo.GetProperty("id").GetGuid()}", admin, null, HttpStatusCode.NoContent);

        // A fan asks for a song; the DJ booth screen shows it (no names).
        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/client/events/{sale.Slug}/song-requests", fan,
            new { title = "Sura Yako", artist = "Sauti Sol" }, HttpStatusCode.Created);

        var booth = await TicketingApi.SendAsync(client, HttpMethod.Post, "/api/admin/display-configurations", admin,
            new { eventId = sale.EventId, name = "DJ screen", slides = new[] { new { kind = "DjBooth", durationSeconds = 30, title = (string?)null, body = (string?)null, assetId = (Guid?)null, kraftIds = Array.Empty<Guid>() } } },
            HttpStatusCode.Created);
        var (_, token) = await DisplaysApi.PairAsync(client, admin, sale.EventId, booth.GetProperty("id").GetGuid(), "DJ booth TV");

        var feed = await DisplaysApi.FeedAsync(client, token);
        var slide = Assert.Single(feed.GetProperty("slides").EnumerateArray());
        Assert.Equal("DjBooth", slide.GetProperty("kind").GetString());
        var queued = Assert.Single(slide.GetProperty("music").GetProperty("queue").EnumerateArray());
        Assert.Equal("Sura Yako", queued.GetProperty("title").GetString());
        Assert.DoesNotContain("name", queued.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<HttpStatusCode> UploadAsync(HttpClient client, IdentityLogin caller, string path, byte[] bytes)
    {
        using var content = new MultipartFormDataContent();
        using var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(file, "file", "image.png");

        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", caller.AccessToken);
        using var response = await client.SendAsync(request);
        return response.StatusCode;
    }
}
