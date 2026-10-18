using System.Net;
using Api.IntegrationTests.Identity.Auditing;
using Api.IntegrationTests.Ticketing;
using Commerce.Domain.Common;
using Identity.Domain.Authorization;
using Identity.Domain.Roles;
using Payments.Domain.Payments;
using Xunit;

namespace Api.IntegrationTests.Commerce;

/// <summary>Kraft onboarding and tills (UF-017): Kraft scope (BR-026), commission visibility, the till test and approval.</summary>
[Collection(IdentityApiCollection.Name)]
public sealed class KraftOnboardingHttpTests(IdentityAuditApiFixture fixture)
{
    private readonly CommerceApi _api = new(fixture);

    [Fact]
    public async Task KraftOwnerManagesOnlyItsOwnKraft()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.Ticketing.AdminAsync(client);
        var sale = await TicketingApi.PublishedEventAsync(client, admin);

        var mine = await CommerceApi.CreateKraftAsync(client, admin, "Mama Pima", sale.EventId, 12.5m);
        var theirs = await CommerceApi.CreateKraftAsync(client, admin, "Mama Pima", sale.EventId);

        var owner = await _api.KraftStaffAsync(client, mine);
        var staff = await _api.KraftStaffAsync(client, mine, IdentityRole.KraftStaff);

        // Same name, distinct slugs.
        var mineView = await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/admin/krafts/{mine}", owner, null, HttpStatusCode.OK);
        var theirsView = await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/admin/krafts/{theirs}", admin, null, HttpStatusCode.OK);
        Assert.Equal("mama-pima", mineView.GetProperty("slug").GetString());
        Assert.Equal("mama-pima-2", theirsView.GetProperty("slug").GetString());

        // The owner keeps its profile, brands and Krafters.
        await TicketingApi.SendAsync(client, HttpMethod.Put, $"/api/admin/krafts/{mine}", owner,
            new { name = "Mama Pima's", discipline = "Food", description = "Githeri, nyama, chapo." }, HttpStatusCode.OK);
        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/krafts/{mine}/brands", owner,
            new { name = "Pima Grill", description = (string?)null }, HttpStatusCode.Created);
        var withKrafter = await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/krafts/{mine}/krafters", owner,
            new { name = "Wanjiru Kamau", role = "Head chef", bio = (string?)null, instagram = "@wanjiru.cooks" }, HttpStatusCode.Created);
        Assert.Equal("wanjiru.cooks", withKrafter.GetProperty("krafters")[0].GetProperty("instagram").GetString());

        // ... but never another Kraft's, and never the tenant-level actions.
        await TicketingApi.SendAsync(client, HttpMethod.Put, $"/api/admin/krafts/{theirs}", owner,
            new { name = "Hijacked", discipline = "Food", description = (string?)null }, HttpStatusCode.Forbidden);
        await TicketingApi.SendAsync(client, HttpMethod.Put, $"/api/admin/krafts/{mine}/events/{sale.EventId}", owner,
            new { commissionPercent = 0m, stall = (string?)null }, HttpStatusCode.Forbidden);
        await TicketingApi.SendAsync(client, HttpMethod.Post, "/api/admin/krafts", owner,
            new { name = "Another", discipline = "Food", description = (string?)null }, HttpStatusCode.Forbidden);

        // Commission: the owner sees its own; Kraft Staff and Support never do.
        var terms = await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/admin/krafts/{mine}/events", owner, null, HttpStatusCode.OK);
        Assert.Equal(12.5m, terms[0].GetProperty("commissionPercent").GetDecimal());
        await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/admin/krafts/{mine}/events", staff, null, HttpStatusCode.Forbidden);

        var support = await _api.Ticketing.AdminAsync(client, IdentityRole.Support);
        var list = await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/admin/krafts?eventId={sale.EventId}", support, null, HttpStatusCode.OK);
        Assert.Equal(2, list.GetArrayLength());
        Assert.DoesNotContain("commission", list.GetRawText(), StringComparison.OrdinalIgnoreCase);
        await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/admin/krafts/{mine}/events", support, null, HttpStatusCode.Forbidden);

        // Public page: profile without commission; nobody takes orders before a till is approved.
        var publicList = await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/client/events/{sale.Slug}/krafts", admin, null, HttpStatusCode.OK);
        var shown = publicList.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == mine);
        Assert.Equal("Courtyard 4", shown.GetProperty("stall").GetString());
        Assert.False(shown.GetProperty("takesOrders").GetBoolean());
        Assert.DoesNotContain("commission", publicList.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ATillTakesMoneyOnlyAfterAPassedTestAndFinanceApproval()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await _api.Ticketing.AdminAsync(client);
        var finance = await _api.Ticketing.AdminAsync(client, IdentityRole.Finance);
        var sale = await TicketingApi.PublishedEventAsync(client, admin);
        var kraftId = await CommerceApi.CreateKraftAsync(client, admin, "Juice Bar", sale.EventId);
        var owner = await _api.KraftStaffAsync(client, kraftId);

        // The owner submits; a second active till for the same event is refused.
        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/krafts/{kraftId}/tills", owner,
            new { eventId = sale.EventId, name = "Juice", shortcodeType = "BuyGoods", shortcode = "12", accountNumber = (string?)null },
            HttpStatusCode.BadRequest);
        var till = await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/krafts/{kraftId}/tills", owner,
            new { eventId = sale.EventId, name = "Juice till", shortcodeType = "BuyGoods", shortcode = "543 210", accountNumber = (string?)null },
            HttpStatusCode.Created);
        var tillId = till.GetProperty("id").GetGuid();
        Assert.Equal("Submitted", till.GetProperty("status").GetString());
        Assert.Equal("543210", till.GetProperty("shortcode").GetString());
        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/krafts/{kraftId}/tills", owner,
            new { eventId = sale.EventId, name = "Second", shortcodeType = "Paybill", shortcode = "522522", accountNumber = "JUICE" },
            HttpStatusCode.Conflict);

        // A Kraft that is not at the event cannot submit one for it.
        var other = await TicketingApi.PublishedEventAsync(client, admin);
        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/krafts/{kraftId}/tills", owner,
            new { eventId = other.EventId, name = "Juice till", shortcodeType = "BuyGoods", shortcode = "543210", accountNumber = (string?)null },
            HttpStatusCode.NotFound);

        // The owner sees its till but cannot reach Finance's queue or decide.
        await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/admin/krafts/{kraftId}/tills", owner, null, HttpStatusCode.OK);
        await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/admin/tills/{tillId}", owner, null, HttpStatusCode.Forbidden);
        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/tills/{tillId}/approve", owner, null, HttpStatusCode.Forbidden);

        // No approval without a passed test.
        var refused = await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/tills/{tillId}/approve", finance, null, HttpStatusCode.Conflict);
        Assert.Equal("Payments.TillTestRequired", refused.GetProperty("errorKey").GetString());

        // A failed test (the prompt was cancelled) ...
        var channelId = Guid.NewGuid();
        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/tills/{tillId}/test", finance,
            new { channelId, phone = finance.Phone }, HttpStatusCode.OK);
        var firstPrompt = fixture.PaymentProvider.PromptsTo(finance.Phone)[^1];
        Assert.Equal(channelId, firstPrompt.Request.ChannelId);
        Assert.Equal(1m, firstPrompt.Request.Amount);
        await _api.Ticketing.CompletePromptAsync(client, firstPrompt, ProviderTransactionStatus.Cancelled);
        await CommerceApi.WaitForTillAsync(client, finance, tillId, "Failed");
        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/tills/{tillId}/approve", finance, null, HttpStatusCode.Conflict);

        // ... then a passing one, and approval.
        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/tills/{tillId}/test", finance,
            new { channelId, phone = finance.Phone }, HttpStatusCode.OK);
        await _api.Ticketing.CompletePromptAsync(client, fixture.PaymentProvider.PromptsTo(finance.Phone)[^1], ProviderTransactionStatus.Success);
        await CommerceApi.WaitForTillAsync(client, finance, tillId, "Passed");

        var approved = await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/tills/{tillId}/approve", finance, null, HttpStatusCode.OK);
        Assert.Equal("Approved", approved.GetProperty("status").GetString());
        Assert.Equal("Juice Bar", approved.GetProperty("kraftName").GetString());

        var queue = await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/admin/tills?status=Approved&eventId={sale.EventId}", finance, null, HttpStatusCode.OK);
        Assert.Contains(queue.EnumerateArray(), x => x.GetProperty("id").GetGuid() == tillId);

        var publicList = await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/client/events/{sale.Slug}/krafts", admin, null, HttpStatusCode.OK);
        Assert.True(publicList.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == kraftId).GetProperty("takesOrders").GetBoolean());

        // Disabling needs a reason; afterwards the Kraft no longer takes orders.
        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/tills/{tillId}/disable", finance, new { note = "" }, HttpStatusCode.BadRequest);
        await TicketingApi.SendAsync(client, HttpMethod.Post, $"/api/admin/tills/{tillId}/disable", finance, new { note = "Till closed by Safaricom" }, HttpStatusCode.OK);
        publicList = await TicketingApi.SendAsync(client, HttpMethod.Get, $"/api/client/events/{sale.Slug}/krafts", admin, null, HttpStatusCode.OK);
        Assert.False(publicList.EnumerateArray().Single(x => x.GetProperty("id").GetGuid() == kraftId).GetProperty("takesOrders").GetBoolean());
    }

    [Fact]
    public void CommercePermissionsAreInTheIdentityCatalog() =>
        Assert.Subset(IdentityPermission.All.ToHashSet(StringComparer.Ordinal), CommercePermission.All.ToHashSet(StringComparer.Ordinal));
}
