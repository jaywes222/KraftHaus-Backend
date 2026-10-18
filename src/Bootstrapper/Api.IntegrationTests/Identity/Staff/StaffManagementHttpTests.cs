using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Api.IntegrationTests.Identity.Auditing;
using Common.Domain.StronglyTypedIds;
using Identity.Application.Auditing;
using Identity.Domain.Common;
using Identity.Domain.Roles;
using Identity.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Api.IntegrationTests.Identity.Staff;

/// <summary>
///     UF-022 through the real host: <c>/me</c>, invitations (create, list, accept with OTP, revoke) and role
///     assignments (grant, list, revoke), all scoped to the tenant resolved from the host name.
/// </summary>
[Collection(IdentityApiCollection.Name)]
public sealed class StaffManagementHttpTests(IdentityAuditApiFixture fixture)
{
    private const string Base = "/api/identity";

    [Fact]
    public async Task Me_ReturnsProfileRolesAndTenantPermissions()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await TenantAdminAsync(client);

        using var response = await IdentityApi.CallAsync(client, HttpMethod.Get, $"{Base}/me", accessToken: admin.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var me = (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone();

        Assert.Equal(admin.UserId, me.GetProperty("userId").GetGuid());
        Assert.Equal(admin.Phone, me.GetProperty("phone").GetString());
        Assert.Equal("Tenant", me.GetProperty("identityScope").GetString());
        Assert.Equal(fixture.TenantId, me.GetProperty("tenantId").GetGuid());
        Assert.Equal(admin.SessionId, me.GetProperty("sessionId").GetString());
        Assert.Contains(me.GetProperty("roles").EnumerateArray(), x => x.GetProperty("role").GetString() == "TenantAdmin");
        Assert.Contains(
            me.GetProperty("tenantPermissions").EnumerateArray(),
            x => x.GetString() == "identity:invitations:create");
    }

    [Fact]
    public async Task Invitation_CreatedByTenantAdmin_AcceptedWithOtp_GrantsTheRole()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await TenantAdminAsync(client);
        var inviteePhone = IdentityApi.NewKenyanPhone();

        var invitationId = await InviteAsync(client, admin.AccessToken, inviteePhone, "Support");

        using (var list = await IdentityApi.CallAsync(client, HttpMethod.Get, $"{Base}/staff/invitations", accessToken: admin.AccessToken))
        {
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            var pending = await list.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains(pending.EnumerateArray(), x => x.GetProperty("invitationId").GetGuid() == invitationId);
        }

        // The invitee has an account (first sign-in), then proves the invited phone with a StaffInvitation OTP.
        var invitee = await IdentityApi.SignInAsync(client, fixture.OtpSender, inviteePhone);
        await RequestInvitationOtpAsync(client, inviteePhone);

        using (var accept = await IdentityApi.CallAsync(
                   client,
                   HttpMethod.Post,
                   $"{Base}/staff/invitations/{invitationId}/accept",
                   new { code = fixture.OtpSender.CodeFor(inviteePhone) }))
        {
            Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        }

        var roles = await RolesOfAsync(client, admin.AccessToken, invitee.UserId);
        var support = Assert.Single(roles);
        Assert.Equal("Support", support.GetProperty("role").GetString());
        Assert.Equal("Tenant", support.GetProperty("scopeType").GetString());
        Assert.Equal(fixture.TenantId, support.GetProperty("scopeId").GetGuid());

        Assert.Equal(1, await AuditCountAsync(IdentityAuditActions.InvitationCreated, invitationId.ToString()));
        Assert.Equal(1, await AuditCountAsync(IdentityAuditActions.InvitationAccepted, invitationId.ToString()));
    }

    [Fact]
    public async Task UserWithoutRole_CannotManageStaff_AndTheDenialIsAuditedAtTenantScope()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var user = await IdentityApi.SignInAsync(client, fixture.OtpSender);

        using var response = await IdentityApi.CallAsync(
            client,
            HttpMethod.Post,
            $"{Base}/staff/invitations",
            new { channel = "Phone", destination = IdentityApi.NewKenyanPhone(), role = "Support" },
            user.AccessToken);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        await using var connection = new NpgsqlConnection(fixture.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT "Metadata"->>'scopeType', "Metadata"->>'scopeId' FROM identity.security_audit_log
            WHERE "Action" = @action AND "SubjectUserId" = @user
            """,
            connection);
        command.Parameters.AddWithValue("action", IdentityAuditActions.AuthorizationDenied);
        command.Parameters.AddWithValue("user", user.UserId);
        await using var reader = await command.ExecuteReaderAsync();

        Assert.True(await reader.ReadAsync());
        Assert.Equal("Tenant", reader.GetString(0));
        Assert.Equal(fixture.TenantId.ToString(), reader.GetString(1));
    }

    [Fact]
    public async Task RoleGrant_ValidatesScopeAndDuplicates_AndRevokeRemovesIt()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await TenantAdminAsync(client);
        var staff = await IdentityApi.SignInAsync(client, fixture.OtpSender);
        var path = $"{Base}/staff/users/{staff.UserId}/roles";

        Guid financeId;
        using (var grant = await IdentityApi.CallAsync(client, HttpMethod.Post, path, new { role = "Finance" }, admin.AccessToken))
        {
            Assert.Equal(HttpStatusCode.Created, grant.StatusCode);
            financeId = (await grant.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("assignmentId").GetGuid();
        }

        await AssertStatusAsync(HttpStatusCode.Conflict, client, HttpMethod.Post, path, new { role = "Finance" }, admin.AccessToken);
        await AssertStatusAsync(HttpStatusCode.Forbidden, client, HttpMethod.Post, path, new { role = "SuperAdmin" }, admin.AccessToken);
        await AssertStatusAsync(HttpStatusCode.BadRequest, client, HttpMethod.Post, path, new { role = "KraftStaff" }, admin.AccessToken);
        await AssertStatusAsync(
            HttpStatusCode.BadRequest,
            client,
            HttpMethod.Post,
            path,
            new { role = "Finance", kraftId = Guid.NewGuid() },
            admin.AccessToken);
        await AssertStatusAsync(
            HttpStatusCode.Created,
            client,
            HttpMethod.Post,
            path,
            new { role = "KraftStaff", kraftId = Guid.NewGuid() },
            admin.AccessToken);

        Assert.Equal(2, (await RolesOfAsync(client, admin.AccessToken, staff.UserId)).Count);

        await AssertStatusAsync(HttpStatusCode.NoContent, client, HttpMethod.Post, $"{Base}/staff/roles/{financeId}/revoke", null, admin.AccessToken);
        await AssertStatusAsync(HttpStatusCode.BadRequest, client, HttpMethod.Post, $"{Base}/staff/roles/{financeId}/revoke", null, admin.AccessToken);

        var remaining = Assert.Single(await RolesOfAsync(client, admin.AccessToken, staff.UserId));
        Assert.Equal("KraftStaff", remaining.GetProperty("role").GetString());

        // Unknown users and assignments are 404.
        await AssertStatusAsync(HttpStatusCode.NotFound, client, HttpMethod.Post, $"{Base}/staff/users/{Guid.NewGuid()}/roles", new { role = "Finance" }, admin.AccessToken);
        await AssertStatusAsync(HttpStatusCode.NotFound, client, HttpMethod.Post, $"{Base}/staff/roles/{Guid.NewGuid()}/revoke", null, admin.AccessToken);
    }

    [Fact]
    public async Task RevokedInvitation_CannotBeAccepted()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await TenantAdminAsync(client);
        var phone = IdentityApi.NewKenyanPhone();
        var invitationId = await InviteAsync(client, admin.AccessToken, phone, "Finance");

        await AssertStatusAsync(HttpStatusCode.Conflict, client, HttpMethod.Post, $"{Base}/staff/invitations",
            new { channel = "Phone", destination = phone, role = "Finance" }, admin.AccessToken);

        var revoke = $"{Base}/staff/invitations/{invitationId}/revoke";
        await AssertStatusAsync(HttpStatusCode.NoContent, client, HttpMethod.Post, revoke, null, admin.AccessToken);
        await AssertStatusAsync(HttpStatusCode.BadRequest, client, HttpMethod.Post, revoke, null, admin.AccessToken);

        await IdentityApi.SignInAsync(client, fixture.OtpSender, phone);
        await RequestInvitationOtpAsync(client, phone);

        await AssertStatusAsync(
            HttpStatusCode.BadRequest,
            client,
            HttpMethod.Post,
            $"{Base}/staff/invitations/{invitationId}/accept",
            new { code = fixture.OtpSender.CodeFor(phone) },
            null);
    }

    [Fact]
    public async Task OtpCode_IssuedOnOneTenantHost_DoesNotSignInOnAnother()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var phone = IdentityApi.NewKenyanPhone();
        var challengeId = await IdentityApi.RequestOtpAsync(client, phone, System.Diagnostics.ActivityTraceId.CreateRandom());

        await AssertStatusAsync(
            HttpStatusCode.NotFound,
            client,
            HttpMethod.Post,
            $"{Base}/otp/verify",
            new
            {
                challengeId,
                code = fixture.OtpSender.CodeFor(phone),
                deviceId = IdentityApi.DeviceId,
                displayName = IdentityApi.DisplayName,
                acceptTerms = true,
                acceptPrivacy = true
            },
            null,
            IdentityAuditApiFixture.SecondTenantHost);

        // The code is still good on the host that issued it.
        await AssertStatusAsync(
            HttpStatusCode.OK,
            client,
            HttpMethod.Post,
            $"{Base}/otp/verify",
            new
            {
                challengeId,
                code = fixture.OtpSender.CodeFor(phone),
                deviceId = IdentityApi.DeviceId,
                displayName = IdentityApi.DisplayName,
                acceptTerms = true,
                acceptPrivacy = true
            },
            null);
    }

    [Fact]
    public async Task TenantAdmin_CannotSeeOrTouchAnotherTenantsStaff()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await TenantAdminAsync(client);
        var invitationId = await InviteAsync(client, admin.AccessToken, IdentityApi.NewKenyanPhone(), "Support");

        // Tenant A's token is refused on tenant B's host before any endpoint runs.
        using (var me = await IdentityApi.CallAsync(
                   client, HttpMethod.Get, $"{Base}/me", accessToken: admin.AccessToken, host: IdentityAuditApiFixture.SecondTenantHost))
        {
            Assert.False(me.IsSuccessStatusCode);
        }

        // A Tenant Admin of tenant B cannot revoke tenant A's invitation (it is invisible to them).
        var bAdmin = await TenantAdminOnSecondTenantAsync(client);
        await AssertStatusAsync(
            HttpStatusCode.NotFound,
            client,
            HttpMethod.Post,
            $"{Base}/staff/invitations/{invitationId}/revoke",
            null,
            bAdmin,
            IdentityAuditApiFixture.SecondTenantHost);
    }

    [Fact]
    public async Task StaffListShowsEveryoneWithARoleHereAndNobodyElse()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var admin = await TenantAdminAsync(client);

        // A Support member joins through an invitation; another person signs in but holds no role.
        var inviteePhone = IdentityApi.NewKenyanPhone();
        var invitationId = await InviteAsync(client, admin.AccessToken, inviteePhone, "Support");
        var invitee = await IdentityApi.SignInAsync(client, fixture.OtpSender, inviteePhone);
        await RequestInvitationOtpAsync(client, inviteePhone);
        using (var accept = await IdentityApi.CallAsync(
                   client,
                   HttpMethod.Post,
                   $"{Base}/staff/invitations/{invitationId}/accept",
                   new { code = fixture.OtpSender.CodeFor(inviteePhone) }))
        {
            Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        }

        var bystander = await IdentityApi.SignInAsync(client, fixture.OtpSender);
        await TenantAdminOnSecondTenantAsync(client);

        using var response = await IdentityApi.CallAsync(client, HttpMethod.Get, $"{Base}/staff/members", accessToken: admin.AccessToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var members = (await response.Content.ReadFromJsonAsync<JsonElement>()).Clone().EnumerateArray().ToList();

        var adminRow = Assert.Single(members, x => x.GetProperty("userId").GetGuid() == admin.UserId);
        Assert.Contains(adminRow.GetProperty("roles").EnumerateArray(), x => x.GetProperty("role").GetString() == "TenantAdmin");

        var inviteeRow = Assert.Single(members, x => x.GetProperty("userId").GetGuid() == invitee.UserId);
        Assert.Equal(invitee.Phone, inviteeRow.GetProperty("phone").GetString());
        var support = Assert.Single(inviteeRow.GetProperty("roles").EnumerateArray());
        Assert.Equal("Support", support.GetProperty("role").GetString());

        Assert.DoesNotContain(members, x => x.GetProperty("userId").GetGuid() == bystander.UserId);

        // Every listed role applies here: a tenant role names this tenant (tenant B's admin is not listed); a Kraft
        // role belongs to a Kraft of this tenant, so its name resolved (other tests in the collection create Krafts).
        Assert.All(
            members.SelectMany(x => x.GetProperty("roles").EnumerateArray()),
            role =>
            {
                if (role.GetProperty("scopeType").GetString() == "Kraft")
                {
                    Assert.False(string.IsNullOrEmpty(role.GetProperty("scopeName").GetString()));
                    return;
                }

                Assert.Equal("Tenant", role.GetProperty("scopeType").GetString());
                Assert.Equal(fixture.TenantId, role.GetProperty("scopeId").GetGuid());
            });

        // Without identity:users:view the list is refused.
        await AssertStatusAsync(HttpStatusCode.Forbidden, client, HttpMethod.Get, $"{Base}/staff/members", null, bystander.AccessToken);
    }

    private async Task<IdentityLogin> TenantAdminAsync(HttpClient client)
    {
        var login = await IdentityApi.SignInAsync(client, fixture.OtpSender);
        await GrantDirectlyAsync(login.UserId, IdentityRole.TenantAdmin, fixture.TenantId);
        return login;
    }

    private async Task<string> TenantAdminOnSecondTenantAsync(HttpClient client)
    {
        var phone = IdentityApi.NewKenyanPhone();
        var host = IdentityAuditApiFixture.SecondTenantHost;

        using var request = await IdentityApi.CallAsync(
            client, HttpMethod.Post, $"{Base}/otp/request", new { channel = "Phone", destination = phone, purpose = "SignIn" }, host: host);
        Assert.Equal(HttpStatusCode.OK, request.StatusCode);
        var challengeId = (await request.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("challengeId").GetString();

        using var verify = await IdentityApi.CallAsync(
            client,
            HttpMethod.Post,
            $"{Base}/otp/verify",
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
        await GrantDirectlyAsync(body.GetProperty("userId").GetGuid(), IdentityRole.TenantAdmin, fixture.SecondTenantId);

        return body.GetProperty("accessToken").GetString()!;
    }

    private async Task GrantDirectlyAsync(Guid userId, IdentityRole role, Guid tenantId)
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

    private static async Task<Guid> InviteAsync(HttpClient client, string accessToken, string phone, string role)
    {
        using var response = await IdentityApi.CallAsync(
            client,
            HttpMethod.Post,
            $"{Base}/staff/invitations",
            new { channel = "Phone", destination = phone, role },
            accessToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("invitationId").GetGuid();
    }

    private static async Task RequestInvitationOtpAsync(HttpClient client, string phone)
    {
        using var response = await IdentityApi.CallAsync(
            client,
            HttpMethod.Post,
            $"{Base}/otp/request",
            new { channel = "Phone", destination = phone, purpose = "StaffInvitation" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<List<JsonElement>> RolesOfAsync(HttpClient client, string accessToken, Guid userId)
    {
        using var response = await IdentityApi.CallAsync(
            client, HttpMethod.Get, $"{Base}/staff/users/{userId}/roles", accessToken: accessToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return [.. body.EnumerateArray().Select(x => x.Clone())];
    }

    private static async Task AssertStatusAsync(
        HttpStatusCode expected,
        HttpClient client,
        HttpMethod method,
        string path,
        object? body,
        string? accessToken,
        string? host = null)
    {
        using var response = await IdentityApi.CallAsync(client, method, path, body, accessToken, host);
        Assert.True(
            expected == response.StatusCode,
            $"{method} {path}: expected {(int)expected}, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    private async Task<long> AuditCountAsync(string action, string targetId)
    {
        await using var connection = new NpgsqlConnection(fixture.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """SELECT count(*) FROM identity.security_audit_log WHERE "Action" = @action AND "TargetId" = @target""",
            connection);
        command.Parameters.AddWithValue("action", action);
        command.Parameters.AddWithValue("target", targetId);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
