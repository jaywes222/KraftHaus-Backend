using System.Runtime.CompilerServices;
using System.Text.Json;
using Common.Application.Auditing;
using Common.Domain.Events;
using Common.Domain.StronglyTypedIds;
using Common.Domain.Tenancy;
using Identity.Application.Auditing;
using Identity.Domain.Common;
using Identity.Domain.Events;
using Identity.Domain.Invitations;
using Identity.Domain.Otp;
using Identity.Domain.Roles;
using Identity.Domain.Sessions;
using Identity.Domain.Users;
using Xunit;

namespace Identity.Tests.Auditing;

public sealed class IdentityAuditEventMapperTests
{
    private const string Phone = "+254712345678";
    private const string Email = "STAFF@EXAMPLE.COM";
    private const string DisplayName = "Wanjiku Kamau";

    private readonly FakeFingerprinter _fingerprinter = new();
    private readonly IdentityAuditEventMapper _mapper;

    public IdentityAuditEventMapperTests()
    {
        _mapper = new IdentityAuditEventMapper(_fingerprinter);
    }

    public static TheoryData<Type> IdentityDomainEventTypes()
    {
        var data = new TheoryData<Type>();

        foreach (var type in typeof(OtpIssued).Assembly
                     .GetTypes()
                     .Where(t => t is { IsClass: true, IsAbstract: false } && t.IsSubclassOf(typeof(DomainEvent)))
                     .OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            data.Add(type);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(IdentityDomainEventTypes))]
    public void Map_EveryIdentityDomainEvent_HasAnExplicitMapping(Type eventType)
    {
        // Built without running a constructor so the test keeps working as event shapes evolve.
        var @event = (DomainEvent)RuntimeHelpers.GetUninitializedObject(eventType);

        var descriptor = _mapper.Map(@event, null);

        Assert.NotEqual(IdentityAuditActions.UnmappedDomainEvent, descriptor.Action);
        Assert.StartsWith(IdentityAuditActions.Module + ".", descriptor.Action, StringComparison.Ordinal);
        Assert.True(descriptor.Action.Length <= 64);
    }

    [Fact]
    public void Map_UnknownEvent_RecordsOnlyItsTypeName()
    {
        var descriptor = _mapper.Map(new UnknownTestEvent(Phone), null);

        Assert.Equal(IdentityAuditActions.UnmappedDomainEvent, descriptor.Action);
        Assert.Equal(["eventType"], descriptor.Metadata.Keys);
        Assert.DoesNotContain(Phone, Json(descriptor), StringComparison.Ordinal);
    }

    [Fact]
    public void Map_OtpIssued_StoresDestinationFingerprintNeverTheDestination()
    {
        var challengeId = new OtpChallengeId(Guid.CreateVersion7());

        var descriptor = _mapper.Map(
            new OtpIssued(challengeId, OtpChannel.Phone, Phone, OtpPurpose.SignIn, DateTimeOffset.UtcNow),
            null);

        Assert.Equal(IdentityAuditActions.OtpIssued, descriptor.Action);
        Assert.Equal(AuditOutcome.Succeeded, descriptor.Outcome);
        Assert.Equal(new AuditTarget(IdentityAuditTargets.OtpChallenge, challengeId.Value.ToString()), descriptor.Target);
        Assert.Equal(_fingerprinter.Fingerprint(Phone), descriptor.Metadata["destinationFingerprint"]);
        Assert.Equal("Phone", descriptor.Metadata["channel"]);
        Assert.Equal("SignIn", descriptor.Metadata["purpose"]);
        Assert.DoesNotContain("712345678", Json(descriptor), StringComparison.Ordinal);
    }

    [Fact]
    public void Map_OtpFailureEvents_AreFailedOrDeniedWithStableReasons()
    {
        var challengeId = new OtpChallengeId(Guid.CreateVersion7());

        var failed = _mapper.Map(new OtpAttemptFailed(challengeId, 3), null);
        var locked = _mapper.Map(new OtpLocked(challengeId), null);
        var expired = _mapper.Map(new OtpExpired(challengeId), null);

        Assert.Equal((IdentityAuditActions.OtpVerificationFailed, AuditOutcome.Failed, "Identity.OtpInvalid"),
            (failed.Action, failed.Outcome, failed.ReasonCode));
        Assert.Equal("3", failed.Metadata["attemptCount"]);
        Assert.Equal((IdentityAuditActions.OtpLocked, AuditOutcome.Denied, "Identity.OtpLocked"),
            (locked.Action, locked.Outcome, locked.ReasonCode));
        Assert.Equal((IdentityAuditActions.OtpExpired, AuditOutcome.Failed, "Identity.OtpExpired"),
            (expired.Action, expired.Outcome, expired.ReasonCode));
    }

    [Fact]
    public void Map_UserRegistered_KeepsIdsAndChannelButNoPersonalData()
    {
        var userId = ApplicationUserId.New();

        var descriptor = _mapper.Map(
            new UserRegistered(userId, IdentityScope.Platform, null, Phone, null, DisplayName),
            null);

        Assert.Equal(IdentityAuditActions.UserRegistered, descriptor.Action);
        Assert.Equal(userId.Value, descriptor.SubjectUserId);
        Assert.Equal("Phone", descriptor.Metadata["verifiedChannel"]);

        var json = Json(descriptor);
        Assert.DoesNotContain("712345678", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Wanjiku", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Map_UserProfileChanged_DoesNotCarryTheNewName()
    {
        var userId = ApplicationUserId.New();

        var descriptor = _mapper.Map(
            new UserProfileChanged(userId, IdentityScope.Platform, null, null, Email, DisplayName),
            null);

        var json = Json(descriptor);
        Assert.DoesNotContain("Wanjiku", json, StringComparison.Ordinal);
        Assert.DoesNotContain("EXAMPLE.COM", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Map_SessionEvents_TakeTheSubjectFromTheSessionAggregate()
    {
        var userId = ApplicationUserId.New();
        var session = AuthSession.Start(
            new AuthSessionId(Guid.CreateVersion7()),
            userId,
            "device-1",
            "hash-1",
            DateTimeOffset.UtcNow).Value!;

        var rotated = _mapper.Map(new SessionTokenRotated(session.Id, 2), session);
        var reuse = _mapper.Map(new SessionRevoked(session.Id, SessionRevocationReason.ReuseDetected), session);
        var signedOut = _mapper.Map(new SessionRevoked(session.Id, SessionRevocationReason.SignedOut), session);

        Assert.Equal(userId.Value, rotated.SubjectUserId);
        Assert.Empty(rotated.Metadata);

        Assert.Equal(IdentityAuditActions.SessionReuseDetected, reuse.Action);
        Assert.Equal(AuditOutcome.Denied, reuse.Outcome);
        Assert.Equal("Identity.SessionReuseDetected", reuse.ReasonCode);
        Assert.Equal(userId.Value, reuse.SubjectUserId);

        Assert.Equal(IdentityAuditActions.SessionRevoked, signedOut.Action);
        Assert.Equal(AuditOutcome.Succeeded, signedOut.Outcome);
        Assert.Equal("SignedOut", signedOut.Metadata["revocationReason"]);
    }

    [Fact]
    public void Map_StaffInvited_FingerprintsTheInviteeDestination()
    {
        var descriptor = _mapper.Map(
            new StaffInvited(
                new StaffInvitationId(Guid.CreateVersion7()),
                ApplicationUserId.New(),
                InvitationChannel.Email,
                Email,
                IdentityRole.Finance,
                RoleScopeType.Tenant,
                Guid.CreateVersion7()),
            null);

        Assert.Equal(IdentityAuditActions.InvitationCreated, descriptor.Action);
        Assert.Equal(_fingerprinter.Fingerprint(Email), descriptor.Metadata["destinationFingerprint"]);
        Assert.Equal("Finance", descriptor.Metadata["role"]);
        Assert.DoesNotContain("EXAMPLE.COM", Json(descriptor), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Map_RealisticEvents_ProduceMetadataTheSanitizerLeavesUntouched()
    {
        // Guards against mapper keys/values that the sanitiser would redact (which would hide real context).
        var userId = ApplicationUserId.New();
        var session = AuthSession.Start(
            new AuthSessionId(Guid.CreateVersion7()), userId, "d", "h", DateTimeOffset.UtcNow).Value!;

        DomainEvent[] events =
        [
            new OtpIssued(new OtpChallengeId(Guid.CreateVersion7()), OtpChannel.Phone, Phone, OtpPurpose.SignIn,
                DateTimeOffset.UtcNow),
            new OtpAttemptFailed(new OtpChallengeId(Guid.CreateVersion7()), 2),
            new UserRegistered(userId, IdentityScope.Tenant, Guid.CreateVersion7().ToString(), null, Email,
                DisplayName),
            new ConsentRecorded(userId, ConsentKind.Privacy, true, "krafthaus-privacy", 3, "abc123"),
            new SessionTokenRotated(session.Id, 12960),
            new SessionRevoked(session.Id, SessionRevocationReason.AdminRevoked),
            new RoleGranted(new RoleAssignmentId(Guid.CreateVersion7()), userId, IdentityRole.KraftStaff,
                RoleScopeType.Kraft, Guid.CreateVersion7()),
            new StaffInvited(new StaffInvitationId(Guid.CreateVersion7()), userId, InvitationChannel.Phone, Phone,
                IdentityRole.Support, RoleScopeType.Tenant, Guid.CreateVersion7())
        ];

        foreach (var @event in events)
        {
            var descriptor = _mapper.Map(@event, session);

            Assert.Equal(
                descriptor.Metadata.OrderBy(x => x.Key, StringComparer.Ordinal),
                AuditMetadataSanitizer.Sanitize(descriptor.Metadata).OrderBy(x => x.Key, StringComparer.Ordinal));
        }
    }

    private static string Json(IdentityAuditDescriptor descriptor) =>
        JsonSerializer.Serialize(descriptor);

    private sealed record UnknownTestEvent(string Secret) : DomainEvent;
}
