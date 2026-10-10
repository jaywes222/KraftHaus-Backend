using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Identity.Application.Auditing;
using Npgsql;
using Xunit;

namespace Api.IntegrationTests.Identity.Auditing;

/// <summary>
///     Security fixes around the session endpoints: bearer authentication on revoke, revoked access tokens stop
///     working, a user can only revoke their own session, and the identity realm comes from the host tenant.
/// </summary>
[Collection(IdentityApiCollection.Name)]
public sealed class IdentitySessionSecurityHttpTests(IdentityAuditApiFixture fixture)
{
    [Fact]
    public async Task Revoke_WithoutBearerToken_IsUnauthorized()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();

        using var response = await IdentityApi.RevokeAsync(client, accessToken: null, ActivityTraceId.CreateRandom());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Revoke_WithGarbageBearerToken_IsUnauthorized()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();

        using var response = await IdentityApi.RevokeAsync(client, "not-a-jwt", ActivityTraceId.CreateRandom());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Revoke_WithBearerToken_RevokesOwnSessionAndAuditsTheUserAsActor()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var login = await SignInAsync(client);
        var trace = ActivityTraceId.CreateRandom();

        using (var revoke = await IdentityApi.RevokeAsync(client, login.AccessToken, trace))
        {
            Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        }

        var row = Assert.Single(
            await AuditRowsAsync(trace),
            x => x.Action == IdentityAuditActions.SessionRevoked);

        Assert.Equal("User", row.ActorType);
        Assert.Equal(login.UserId.ToString(), row.ActorId);
        Assert.Equal(login.SessionId, row.TargetId);
        Assert.Equal(fixture.TenantId, row.TenantId);
        Assert.Equal("Revoked", await SessionStatusAsync(login.SessionId));

        // The revoked session's refresh token is dead too.
        using var refresh = await IdentityApi.RefreshAsync(
            client,
            login.SessionId,
            login.RefreshToken,
            ActivityTraceId.CreateRandom());

        Assert.NotEqual(HttpStatusCode.OK, refresh.StatusCode);
    }

    [Fact]
    public async Task AccessToken_OfARevokedSession_IsRejectedImmediately()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var login = await SignInAsync(client);

        using (var first = await IdentityApi.RevokeAsync(client, login.AccessToken, ActivityTraceId.CreateRandom()))
        {
            Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        }

        // The JWT is still cryptographically valid and unexpired; the session check must refuse it anyway.
        using var reused = await IdentityApi.RevokeAsync(client, login.AccessToken, ActivityTraceId.CreateRandom());

        Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);
    }

    [Fact]
    public async Task Revoke_ByAnotherUser_LeavesTheFirstUsersSessionActive()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var alice = await SignInAsync(client);
        var bob = await SignInAsync(client);

        using (var revoke = await IdentityApi.RevokeAsync(client, bob.AccessToken, ActivityTraceId.CreateRandom()))
        {
            Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);
        }

        Assert.Equal("Active", await SessionStatusAsync(alice.SessionId));
        Assert.Equal("Revoked", await SessionStatusAsync(bob.SessionId));

        using var refresh = await IdentityApi.RefreshAsync(
            client,
            alice.SessionId,
            alice.RefreshToken,
            ActivityTraceId.CreateRandom());

        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
    }

    [Fact]
    public async Task AccessToken_CarriesPlainGuidSubjectSessionTenantAndClientClaims()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var login = await SignInAsync(client);

        var payload = login.AccessToken.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');

        using var claims = JsonDocument.Parse(Convert.FromBase64String(payload));
        var root = claims.RootElement;

        Assert.Equal(login.UserId.ToString(), root.GetProperty("sub").GetString());
        Assert.Equal(login.SessionId, root.GetProperty("sid").GetString());
        Assert.Equal(fixture.TenantId.ToString(), root.GetProperty("tid").GetString());
        Assert.Equal("krafthaus-otp", root.GetProperty("client_id").GetString());
    }

    [Fact]
    public async Task Verify_RegistersTheUserInTheHostTenantRealm()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var login = await SignInAsync(client);

        await using var connection = new NpgsqlConnection(fixture.DatabaseConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT "Scope", "TenantId", "CreatedOn"
            FROM identity.application_user
            WHERE "Id" = @id
            """,
            connection);
        command.Parameters.AddWithValue("id", login.UserId);

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());

        Assert.Equal("Tenant", reader.GetString(0));
        Assert.Equal(fixture.TenantId, reader.GetGuid(1));
        Assert.Equal(
            fixture.Clock.GetUtcNow(),
            await reader.GetFieldValueAsync<DateTimeOffset>(2),
            TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Verify_WithClientSuppliedScope_IsRejected()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var phone = IdentityApi.NewKenyanPhone();
        var trace = ActivityTraceId.CreateRandom();
        var challengeId = await IdentityApi.RequestOtpAsync(client, phone, trace);

        using var response = await IdentityApi.SendAsync(
            client,
            "/api/identity/otp/verify",
            new
            {
                challengeId,
                code = fixture.OtpSender.CodeFor(phone),
                deviceId = IdentityApi.DeviceId,
                displayName = IdentityApi.DisplayName,
                acceptTerms = true,
                acceptPrivacy = true,
                scope = "Platform"
            },
            trace);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<Login> SignInAsync(HttpClient client)
    {
        var phone = IdentityApi.NewKenyanPhone();
        var trace = ActivityTraceId.CreateRandom();
        var challengeId = await IdentityApi.RequestOtpAsync(client, phone, trace);

        using var response = await IdentityApi.VerifyAsync(client, challengeId, fixture.OtpSender.CodeFor(phone), trace);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        return new Login(
            body.GetProperty("userId").GetGuid(),
            body.GetProperty("sessionId").GetString()!,
            body.GetProperty("accessToken").GetString()!,
            body.GetProperty("refreshToken").GetString()!);
    }

    private async Task<string> SessionStatusAsync(string sessionId)
    {
        await using var connection = new NpgsqlConnection(fixture.DatabaseConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """SELECT "Status" FROM identity.auth_session WHERE "Id" = @id""",
            connection);
        command.Parameters.AddWithValue("id", Guid.Parse(sessionId));

        return (string)(await command.ExecuteScalarAsync())!;
    }

    private async Task<List<AuditRow>> AuditRowsAsync(ActivityTraceId trace)
    {
        await using var connection = new NpgsqlConnection(fixture.DatabaseConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT "Action", "ActorType", "ActorId", "TenantId", "TargetId"
            FROM identity.security_audit_log
            WHERE "TraceId" = @trace
            """,
            connection);
        command.Parameters.AddWithValue("trace", trace.ToHexString());

        var rows = new List<AuditRow>();
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new AuditRow(
                reader.GetString(0),
                reader.GetString(1),
                await reader.IsDBNullAsync(2) ? null : reader.GetString(2),
                await reader.IsDBNullAsync(3) ? null : reader.GetGuid(3),
                await reader.IsDBNullAsync(4) ? null : reader.GetString(4)));
        }

        return rows;
    }

    private sealed record Login(Guid UserId, string SessionId, string AccessToken, string RefreshToken);

    private sealed record AuditRow(string Action, string ActorType, string? ActorId, Guid? TenantId, string? TargetId);
}
