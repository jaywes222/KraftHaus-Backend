using System.Net;
using System.Text.Json;
using Common.Application.Auditing;
using Common.Domain.ResultMonad;
using Common.Domain.StronglyTypedIds;
using Identity.Application.Auditing;
using Identity.Domain.Common;
using Identity.Domain.Events;
using Identity.Infrastructure.Auditing;
using Xunit;

namespace Identity.Tests.Auditing;

public sealed class IdentityAuditEntryTests
{
    private static readonly DateTimeOffset _now = new(2026, 10, 9, 8, 30, 0, TimeSpan.Zero);

    private readonly FakeFingerprinter _fingerprinter = new();

    [Fact]
    public void FromEvent_UsesEventIdRequestContextAndClock()
    {
        var userId = Guid.CreateVersion7();
        var tenantId = Guid.CreateVersion7();
        var context = new AuditRequestContext(
            AuditActor.User(userId.ToString(), "sid-1"),
            tenantId,
            "4bf92f3577b34da6a3ce929d0e0e4736",
            "req-1");

        var factory = CreateFactory(context);
        var @event = new SessionStarted(new AuthSessionId(Guid.CreateVersion7()), new ApplicationUserId(userId));
        var descriptor = new IdentityAuditEventMapper(_fingerprinter).Map(@event, null);

        var entry = factory.FromEvent(@event, descriptor);

        Assert.Equal(@event.Id, entry.Id);
        Assert.Equal(_now, entry.OccurredAt);
        Assert.Equal(IdentityAuditActions.Module, entry.Module);
        Assert.Equal(IdentityAuditActions.SessionStarted, entry.Action);
        Assert.Equal(context.Actor, entry.Actor);
        Assert.Equal(tenantId, entry.TenantId);
        Assert.Equal(userId, entry.SubjectUserId);
        Assert.Equal("4bf92f3577b34da6a3ce929d0e0e4736", entry.TraceId);
        Assert.Equal("req-1", entry.RequestId);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, AuditOutcome.Failed)]
    [InlineData(HttpStatusCode.NotFound, AuditOutcome.Failed)]
    [InlineData(HttpStatusCode.Unauthorized, AuditOutcome.Denied)]
    [InlineData(HttpStatusCode.Forbidden, AuditOutcome.Denied)]
    public void FromRejection_MapsStatusCodeToOutcome(HttpStatusCode status, AuditOutcome expected)
    {
        var entry = CreateFactory(AuditRequestContext.System(null))
            .FromRejection(new IdentityAuditRejection("identity.test.rejected", new Error { Key = "K", StatusCode = status }));

        Assert.Equal(expected, entry.Outcome);
    }

    [Fact]
    public void FromRejection_StoresErrorKeyOnlyAndFingerprintsDestination()
    {
        var entry = CreateFactory(new AuditRequestContext(AuditActor.Anonymous, null, null, "req-2"))
            .FromRejection(
                new IdentityAuditRejection(
                    IdentityAuditActions.OtpRequestRejected,
                    new Error
                    {
                        Key = "Identity.PhoneNumberInvalid",
                        Description = "Phone number 0712345678 is not valid.",
                        Value = "0712345678"
                    })
                {
                    DestinationToFingerprint = "0712345678",
                    Metadata = new Dictionary<string, string> { ["channel"] = "Phone" }
                });

        Assert.Equal("Identity.PhoneNumberInvalid", entry.ReasonCode);
        Assert.Equal(AuditActor.Anonymous, entry.Actor);
        Assert.Equal(_fingerprinter.Fingerprint("0712345678"), entry.Metadata["destinationFingerprint"]);
        Assert.Equal("Phone", entry.Metadata["channel"]);
        Assert.DoesNotContain("712345678", JsonSerializer.Serialize(entry), StringComparison.Ordinal);
    }

    [Fact]
    public void RecordFrom_SanitizesMetadataAndTruncatesColumns()
    {
        var entry = new AuditEntry
        {
            Id = Guid.CreateVersion7(),
            OccurredAt = _now,
            Module = IdentityAuditActions.Module,
            Action = IdentityAuditActions.SignInRejected,
            Outcome = AuditOutcome.Failed,
            Actor = AuditActor.Anonymous,
            ReasonCode = new string('R', 500),
            Target = new AuditTarget(IdentityAuditTargets.OtpChallenge, new string('T', 500)),
            Metadata = new Dictionary<string, string>
            {
                ["code"] = "123456",
                ["refreshToken"] = "0199c3a1-6f2e-7a10-9b55-0e4f3b2a1c00.1.abcdefghijklmnopqrstuvwxyz",
                ["note"] = "eyJhbGciOiJFUzI1NiJ9.eyJzdWIiOiIxMjMifQ.c2ln",
                ["channel"] = "Phone"
            }
        };

        var record = IdentityAuditRecord.From(entry);

        Assert.Equal(IdentityAuditRecord.ReasonCodeMaxLength, record.ReasonCode!.Length);
        Assert.Equal(IdentityAuditRecord.TargetIdMaxLength, record.TargetId!.Length);
        Assert.Equal(AuditMetadataSanitizer.Redacted, record.Metadata["code"]);
        Assert.Equal(AuditMetadataSanitizer.Redacted, record.Metadata["refreshToken"]);
        Assert.Equal(AuditMetadataSanitizer.Redacted, record.Metadata["note"]);
        Assert.Equal("Phone", record.Metadata["channel"]);
        Assert.Equal("Failed", record.Outcome);
        Assert.Equal("Anonymous", record.ActorType);
    }

    private IdentityAuditEntryFactory CreateFactory(AuditRequestContext context) =>
        new(new StubRequestContext(context), _fingerprinter, new FixedTime(_now));

    private sealed class StubRequestContext(AuditRequestContext context) : IAuditRequestContextProvider
    {
        public AuditRequestContext GetCurrent() => context;
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
