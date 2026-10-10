using Common.Domain.Tenancy;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Identity.Application.Auditing;
using Identity.Domain.Common;
using Identity.Domain.Otp;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace Api.IntegrationTests.Identity.Auditing;

/// <summary>
///     WP-08.4: end-to-end audit behaviour through the real host pipeline (tenant resolution, rate limiting,
///     endpoint filters, EF interceptor, PostgreSQL trigger).
/// </summary>
[Collection(IdentityApiCollection.Name)]
public sealed class IdentityAuditHttpTests(IdentityAuditApiFixture fixture)
{
    [Fact]
    public async Task SignInFlow_RecordsSecurityEventsWithActorTenantAndTraceId()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var phone = IdentityApi.NewKenyanPhone();

        // 1. OTP request.
        var requestTrace = ActivityTraceId.CreateRandom();
        var challengeId = await IdentityApi.RequestOtpAsync(client, phone, requestTrace);

        var issued = Assert.Single(await RowsAsync(requestTrace));
        Assert.Equal(IdentityAuditActions.OtpIssued, issued.Action);
        Assert.Equal("Succeeded", issued.Outcome);
        Assert.Equal("Anonymous", issued.ActorType);
        Assert.Null(issued.ActorId);
        Assert.Equal(fixture.TenantId, issued.TenantId);
        Assert.Equal(IdentityAuditTargets.OtpChallenge, issued.TargetType);
        Assert.Equal(challengeId, issued.TargetId);
        Assert.NotNull(issued.RequestId);
        Assert.Matches("^[0-9A-F]{64}$", issued.MetadataValue("destinationFingerprint"));

        // 2. Wrong code: the failed attempt (state change) and the refused sign-in are both recorded.
        var wrongTrace = ActivityTraceId.CreateRandom();
        using (var wrong = await IdentityApi.VerifyAsync(client, challengeId, WrongCode(phone), wrongTrace))
        {
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        }

        var wrongRows = await RowsAsync(wrongTrace);
        Assert.Equal(
            [IdentityAuditActions.OtpVerificationFailed, IdentityAuditActions.SignInRejected],
            wrongRows.Select(x => x.Action).Order(StringComparer.Ordinal));
        var rejected = wrongRows.Single(x => x.Action == IdentityAuditActions.SignInRejected);
        Assert.Equal("Failed", rejected.Outcome);
        Assert.Equal("Identity.OtpInvalid", rejected.ReasonCode);
        Assert.Equal(challengeId, rejected.TargetId);

        // 3. Correct code: registration, consents and the new session land in one transaction.
        var verifyTrace = ActivityTraceId.CreateRandom();
        using var verify = await IdentityApi.VerifyAsync(client, challengeId, fixture.OtpSender.CodeFor(phone), verifyTrace);
        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);

        var login = await verify.Content.ReadFromJsonAsync<JsonElement>();
        var userId = login.GetProperty("userId").GetGuid();
        var sessionId = login.GetProperty("sessionId").GetString();

        var loginRows = await RowsAsync(verifyTrace);
        Assert.Equal(
            [
                IdentityAuditActions.ConsentRecorded,
                IdentityAuditActions.ConsentRecorded,
                IdentityAuditActions.OtpVerified,
                IdentityAuditActions.SessionStarted,
                IdentityAuditActions.UserRegistered
            ],
            loginRows.Select(x => x.Action).Order(StringComparer.Ordinal));
        Assert.All(loginRows, x => Assert.Equal(fixture.TenantId, x.TenantId));
        Assert.Single(loginRows.Select(x => x.OccurredAt).Distinct());

        var started = loginRows.Single(x => x.Action == IdentityAuditActions.SessionStarted);
        Assert.Equal(userId, started.SubjectUserId);
        Assert.Equal(IdentityAuditTargets.AuthSession, started.TargetType);
        Assert.Equal(sessionId, started.TargetId);
        Assert.Equal(userId, loginRows.Single(x => x.Action == IdentityAuditActions.UserRegistered).SubjectUserId);
    }

    [Fact]
    public async Task RefreshTokenReuse_RecordsReuseDetectionAndRejectedRefresh()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var login = await SignInAsync(client, IdentityApi.NewKenyanPhone());

        var rotateTrace = ActivityTraceId.CreateRandom();
        using (var rotated = await IdentityApi.RefreshAsync(client, login.SessionId, login.RefreshToken, rotateTrace))
        {
            Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        }

        var rotation = Assert.Single(await RowsAsync(rotateTrace));
        Assert.Equal(IdentityAuditActions.SessionRotated, rotation.Action);
        Assert.Equal(login.UserId, rotation.SubjectUserId);

        // Replay the first refresh token after the 10 second grace window.
        fixture.Clock.Advance(TimeSpan.FromSeconds(11));

        var reuseTrace = ActivityTraceId.CreateRandom();
        using (var reused = await IdentityApi.RefreshAsync(client, login.SessionId, login.RefreshToken, reuseTrace))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, reused.StatusCode);
        }

        var reuseRows = await RowsAsync(reuseTrace);
        var detected = reuseRows.Single(x => x.Action == IdentityAuditActions.SessionReuseDetected);
        var refused = reuseRows.Single(x => x.Action == IdentityAuditActions.SessionRefreshRejected);

        Assert.Equal("Denied", detected.Outcome);
        Assert.Equal(login.UserId, detected.SubjectUserId);
        Assert.Equal("Denied", refused.Outcome);
        Assert.Equal("Identity.SessionReuseDetected", refused.ReasonCode);
        Assert.Equal(login.SessionId, refused.TargetId);
    }

    [Fact]
    public async Task AuditRows_NeverContainOtpCodesTokensOrPersonalData()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var phone = IdentityApi.NewKenyanPhone();
        var trace = ActivityTraceId.CreateRandom();

        var challengeId = await IdentityApi.RequestOtpAsync(client, phone, trace);
        var code = fixture.OtpSender.CodeFor(phone);
        var wrongCode = WrongCode(phone);

        (await IdentityApi.VerifyAsync(client, challengeId, wrongCode, trace)).Dispose();

        using var verify = await IdentityApi.VerifyAsync(client, challengeId, code, trace);
        var login = await verify.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = login.GetProperty("accessToken").GetString()!;
        var refreshToken = login.GetProperty("refreshToken").GetString()!;
        var sessionId = login.GetProperty("sessionId").GetString()!;

        (await IdentityApi.RefreshAsync(client, sessionId, refreshToken, trace)).Dispose();

        // Garbage refresh token: rejected without touching any session.
        (await IdentityApi.RefreshAsync(client, sessionId, "not-a-token." + refreshToken, trace)).Dispose();

        var rows = await RowsAsync(trace);
        Assert.True(rows.Count >= 8, $"Expected the whole flow to be audited, got {rows.Count} rows.");

        string[] forbidden =
        [
            code,
            wrongCode,
            phone[1..],
            phone[4..],
            "0" + phone[4..],
            IdentityApi.DisplayName,
            "Wanjiku",
            accessToken,
            .. accessToken.Split('.').Where(x => x.Length > 8),
            refreshToken,
            refreshToken.Split('.')[^1],
            "Bearer"
        ];

        foreach (var row in rows)
        {
            foreach (var value in row.FreeTextValues())
            {
                foreach (var secret in forbidden)
                {
                    Assert.False(
                        value.Contains(secret, StringComparison.OrdinalIgnoreCase),
                        $"Audit row {row.Action} leaked a sensitive value in '{value}'.");
                }
            }
        }
    }

    [Fact]
    public async Task AuditLog_IsAppendOnly_UpdateDeleteAndTruncateAreRejected()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        using var client = fixture.CreateClient();
        var trace = ActivityTraceId.CreateRandom();
        await IdentityApi.RequestOtpAsync(client, IdentityApi.NewKenyanPhone(), trace);
        var row = Assert.Single(await RowsAsync(trace));

        await using var connection = new NpgsqlConnection(fixture.DatabaseConnectionString);
        await connection.OpenAsync();

        foreach (var sql in new[]
                 {
                     """UPDATE identity.security_audit_log SET "Outcome" = 'Failed' WHERE "Id" = @id""",
                     """DELETE FROM identity.security_audit_log WHERE "Id" = @id""",
                     "TRUNCATE identity.security_audit_log"
                 })
        {
#pragma warning disable CA2100 // Constant test SQL.
            await using var command = new NpgsqlCommand(sql, connection);
#pragma warning restore CA2100
            command.Parameters.AddWithValue("id", row.Id);

            var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
        }

        // A retention purge must opt in explicitly, per transaction (rolled back here to keep the row).
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var optIn = new NpgsqlCommand("SET LOCAL krafthaus.audit_purge = 'on'", connection, transaction))
        {
            await optIn.ExecuteNonQueryAsync();
        }

        await using (var purge = new NpgsqlCommand(
                         """DELETE FROM identity.security_audit_log WHERE "Id" = @id""", connection, transaction))
        {
            purge.Parameters.AddWithValue("id", row.Id);
            Assert.Equal(1, await purge.ExecuteNonQueryAsync());
        }

        await transaction.RollbackAsync();
        Assert.Single(await RowsAsync(trace));
    }

    [Fact]
    public async Task DomainEventCapture_IsAtomicWithTheStateChangeAndNeverDuplicated()
    {
        Assert.SkipUnless(fixture.IsConfigured, IdentityAuditApiFixture.SkipReason);

        // If the audit row cannot be written, the state change must not be written either.
        var blocked = IssueChallenge();
        var blockedEventId = blocked.Events.Single().Id;
        await InsertConflictingAuditRowAsync(blockedEventId);

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.OtpChallenges.Add(blocked);

            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        Assert.Equal(0, await CountAsync("""SELECT count(*) FROM identity.otp_challenge WHERE "Id" = @id""", blocked.Id.Value));
        Assert.Single(blocked.Events);

        // Happy path: exactly one row per event, events cleared, a second save adds nothing.
        var challenge = IssueChallenge();
        var eventId = challenge.Events.Single().Id;

        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            db.OtpChallenges.Add(challenge);

            await db.SaveChangesAsync();
            Assert.Empty(challenge.Events);

            await db.SaveChangesAsync();
        }

        Assert.Equal(1, await CountAsync("""SELECT count(*) FROM identity.security_audit_log WHERE "Id" = @id""", eventId));

        var row = (await RowsAsync(null)).Single(x => x.Id == eventId);
        Assert.Equal("System", row.ActorType);
        Assert.Equal(IdentityAuditActions.OtpIssued, row.Action);
        Assert.Equal(challenge.Id.Value.ToString(), row.TargetId);
    }

    private OtpChallenge IssueChallenge() =>
        OtpChallenge.Issue(
            new OtpChallengeId(Guid.CreateVersion7()),
            null,
            OtpChannel.Phone,
            IdentityApi.NewKenyanPhone(),
            OtpPurpose.SignIn,
            new string('A', 64),
            DateTimeOffset.UtcNow,
            new TenantId(fixture.TenantId)).Value!;

    private async Task InsertConflictingAuditRowAsync(Guid id)
    {
        await using var connection = new NpgsqlConnection(fixture.DatabaseConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            INSERT INTO identity.security_audit_log
                ("Id", "OccurredAt", "Module", "Action", "Outcome", "ActorType", "Metadata")
            VALUES (@id, now(), 'identity', 'test.conflict', 'Succeeded', 'System', '{}'::jsonb)
            """,
            connection);
        command.Parameters.AddWithValue("id", id);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> CountAsync(string sql, Guid id)
    {
        await using var connection = new NpgsqlConnection(fixture.DatabaseConnectionString);
        await connection.OpenAsync();
#pragma warning disable CA2100 // Constant test SQL.
        await using var command = new NpgsqlCommand(sql, connection);
#pragma warning restore CA2100
        command.Parameters.AddWithValue("id", id);

        return (long)(await command.ExecuteScalarAsync())!;
    }

    private async Task<(Guid UserId, string SessionId, string RefreshToken)> SignInAsync(
        HttpClient client,
        string phone)
    {
        var trace = ActivityTraceId.CreateRandom();
        var challengeId = await IdentityApi.RequestOtpAsync(client, phone, trace);

        using var response = await IdentityApi.VerifyAsync(client, challengeId, fixture.OtpSender.CodeFor(phone), trace);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        return (
            body.GetProperty("userId").GetGuid(),
            body.GetProperty("sessionId").GetString()!,
            body.GetProperty("refreshToken").GetString()!);
    }

    private async Task<List<AuditRow>> RowsAsync(ActivityTraceId? trace)
    {
        await using var connection = new NpgsqlConnection(fixture.DatabaseConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(
            """
            SELECT "Id", "OccurredAt", "Action", "Outcome", "ActorType", "ActorId", "SubjectUserId", "TenantId",
                   "TargetType", "TargetId", "ReasonCode", "TraceId", "RequestId", "Metadata"::text
            FROM identity.security_audit_log
            WHERE @trace IS NULL OR "TraceId" = @trace
            ORDER BY "OccurredAt", "Id"
            """,
            connection);

        command.Parameters.Add(new NpgsqlParameter("trace", NpgsqlTypes.NpgsqlDbType.Varchar)
        {
            Value = trace is null ? DBNull.Value : trace.Value.ToHexString()
        });

        var rows = new List<AuditRow>();
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new AuditRow(
                await reader.GetFieldValueAsync<Guid>(0),
                await reader.GetFieldValueAsync<DateTimeOffset>(1),
                await reader.GetFieldValueAsync<string>(2),
                await reader.GetFieldValueAsync<string>(3),
                await reader.GetFieldValueAsync<string>(4),
                await NullableAsync<string>(reader, 5),
                await NullableStructAsync<Guid>(reader, 6),
                await NullableStructAsync<Guid>(reader, 7),
                await NullableAsync<string>(reader, 8),
                await NullableAsync<string>(reader, 9),
                await NullableAsync<string>(reader, 10),
                await NullableAsync<string>(reader, 11),
                await NullableAsync<string>(reader, 12),
                await reader.GetFieldValueAsync<string>(13)));
        }

        return rows;
    }

    private static async Task<T?> NullableAsync<T>(NpgsqlDataReader reader, int ordinal)
        where T : class =>
        await reader.IsDBNullAsync(ordinal) ? null : await reader.GetFieldValueAsync<T>(ordinal);

    private static async Task<T?> NullableStructAsync<T>(NpgsqlDataReader reader, int ordinal)
        where T : struct =>
        await reader.IsDBNullAsync(ordinal) ? null : await reader.GetFieldValueAsync<T>(ordinal);

    private string WrongCode(string phone)
    {
        // Only valid after the OTP request: guaranteed to differ from the real code.
        var real = fixture.OtpSender.CodeFor(phone);

        return real == "000000" ? "111111" : "000000";
    }

    private sealed record AuditRow(
        Guid Id,
        DateTimeOffset OccurredAt,
        string Action,
        string Outcome,
        string ActorType,
        string? ActorId,
        Guid? SubjectUserId,
        Guid? TenantId,
        string? TargetType,
        string? TargetId,
        string? ReasonCode,
        string? TraceId,
        string? RequestId,
        string Metadata)
    {
        public string? MetadataValue(string key) =>
            JsonDocument.Parse(Metadata).RootElement.TryGetProperty(key, out var value)
                ? value.GetString()
                : null;

        /// <summary>
        ///     Every string the row stores, minus the fields that are opaque ids by construction (GUIDs, hex trace ids,
        ///     HMAC fingerprints). Those cannot carry a secret, but a 6-digit OTP could appear inside them by chance.
        /// </summary>
        public IEnumerable<string> FreeTextValues()
        {
            string?[] candidates = [Action, Outcome, ActorType, ActorId, TargetType, TargetId, ReasonCode, RequestId];

            foreach (var value in candidates.Where(x => x is not null && !IsOpaqueId(x)))
            {
                yield return value!;
            }

            using var document = JsonDocument.Parse(Metadata);

            foreach (var property in document.RootElement.EnumerateObject())
            {
                yield return property.Name;

                var value = property.Value.GetString();

                if (value is not null && !IsOpaqueId(value))
                {
                    yield return value;
                }
            }
        }

        private static bool IsOpaqueId(string value) =>
            Guid.TryParse(value, out _) ||
            (value.Length is 32 or 64 && value.All(Uri.IsHexDigit));
    }
}
