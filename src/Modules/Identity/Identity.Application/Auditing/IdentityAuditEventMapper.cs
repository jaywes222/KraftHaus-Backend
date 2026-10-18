using System.Globalization;
using Common.Application.Auditing;
using Common.Domain.Events;
using Identity.Domain.Badges;
using Identity.Domain.Events;
using Identity.Domain.Sessions;

namespace Identity.Application.Auditing;

/// <summary>
///     What an Identity domain event means for the audit trail: action, outcome, target, subject and a small,
///     allow-listed set of metadata. Actor, tenant, time and correlation are added by the capture pipeline.
/// </summary>
public sealed record IdentityAuditDescriptor(
    string Action,
    AuditOutcome Outcome,
    AuditTarget? Target)
{
    public Guid? SubjectUserId { get; init; }

    public string? ReasonCode { get; init; }

    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}

/// <summary>
///     Maps Identity domain events to audit descriptors (WP-08.1 / WP-08.3).
///     <para>
///         Every field is chosen explicitly. Events that carry personal data (<see cref="OtpIssued.Destination" />,
///         <see cref="UserRegistered.Phone" />, <see cref="UserRegistered.Email" />, display names,
///         <see cref="StaffInvited.Destination" />) contribute only a keyed fingerprint or nothing at all. OTP codes,
///         code hashes and refresh-token secrets never reach domain events in the first place.
///     </para>
///     <para>
///         <c>IdentityAuditEventMapperTests</c> fails when a new Identity domain event is added without a mapping
///         here, so new security-relevant state changes cannot silently skip the audit trail.
///     </para>
/// </summary>
public sealed class IdentityAuditEventMapper(
    IAuditFingerprinter fingerprinter)
{
    /// <param name="event">The domain event raised by an Identity aggregate.</param>
    /// <param name="source">The aggregate that raised it (used for context the event itself does not carry).</param>
    public IdentityAuditDescriptor Map(
        DomainEvent @event,
        object? source)
    {
        ArgumentNullException.ThrowIfNull(@event);

        return @event switch
        {
            OtpIssued e => new IdentityAuditDescriptor(
                IdentityAuditActions.OtpIssued,
                AuditOutcome.Succeeded,
                OtpTarget(e.ChallengeId.Value))
            {
                Metadata = Meta(
                    ("channel", e.Channel.ToString()),
                    ("purpose", e.Purpose.ToString()),
                    ("destinationFingerprint", fingerprinter.Fingerprint(e.Destination)))
            },

            OtpVerified e => new IdentityAuditDescriptor(
                IdentityAuditActions.OtpVerified,
                AuditOutcome.Succeeded,
                OtpTarget(e.ChallengeId.Value)),

            OtpAttemptFailed e => new IdentityAuditDescriptor(
                IdentityAuditActions.OtpVerificationFailed,
                AuditOutcome.Failed,
                OtpTarget(e.ChallengeId.Value))
            {
                ReasonCode = "Identity.OtpInvalid",
                Metadata = Meta(("attemptCount", Number(e.AttemptCount)))
            },

            OtpLocked e => new IdentityAuditDescriptor(
                IdentityAuditActions.OtpLocked,
                AuditOutcome.Denied,
                OtpTarget(e.ChallengeId.Value))
            {
                ReasonCode = "Identity.OtpLocked"
            },

            OtpExpired e => new IdentityAuditDescriptor(
                IdentityAuditActions.OtpExpired,
                AuditOutcome.Failed,
                OtpTarget(e.ChallengeId.Value))
            {
                ReasonCode = "Identity.OtpExpired"
            },

            OtpSuperseded e => new IdentityAuditDescriptor(
                IdentityAuditActions.OtpSuperseded,
                AuditOutcome.Succeeded,
                OtpTarget(e.ChallengeId.Value)),

            UserRegistered e => new IdentityAuditDescriptor(
                IdentityAuditActions.UserRegistered,
                AuditOutcome.Succeeded,
                UserTarget(e.UserId.Value))
            {
                SubjectUserId = e.UserId.Value,
                Metadata = Meta(
                    ("identityScope", e.Scope.ToString()),
                    ("verifiedChannel", e.Phone is not null ? "Phone" : "Email"),
                    ("userTenantId", e.TenantId))
            },

            UserProfileChanged e => new IdentityAuditDescriptor(
                IdentityAuditActions.UserProfileChanged,
                AuditOutcome.Succeeded,
                UserTarget(e.UserId.Value))
            {
                SubjectUserId = e.UserId.Value
            },

            ConsentRecorded e => new IdentityAuditDescriptor(
                IdentityAuditActions.ConsentRecorded,
                AuditOutcome.Succeeded,
                UserTarget(e.UserId.Value))
            {
                SubjectUserId = e.UserId.Value,
                Metadata = Meta(
                    ("consentKind", e.Kind.ToString()),
                    ("granted", e.Granted ? "true" : "false"),
                    ("policy", e.PolicyKey),
                    ("policyVersion", Number(e.PolicyVersion)))
            },

            UserSignedIn e => new IdentityAuditDescriptor(
                IdentityAuditActions.UserSignedIn,
                AuditOutcome.Succeeded,
                UserTarget(e.UserId.Value))
            {
                SubjectUserId = e.UserId.Value
            },

            UserDisabled e => new IdentityAuditDescriptor(
                IdentityAuditActions.UserDisabled,
                AuditOutcome.Succeeded,
                UserTarget(e.UserId.Value))
            {
                SubjectUserId = e.UserId.Value
            },

            UserEnabled e => new IdentityAuditDescriptor(
                IdentityAuditActions.UserEnabled,
                AuditOutcome.Succeeded,
                UserTarget(e.UserId.Value))
            {
                SubjectUserId = e.UserId.Value
            },

            SessionStarted e => new IdentityAuditDescriptor(
                IdentityAuditActions.SessionStarted,
                AuditOutcome.Succeeded,
                SessionTarget(e.SessionId.Value))
            {
                SubjectUserId = e.UserId.Value
            },

            SessionTokenRotated e => new IdentityAuditDescriptor(
                IdentityAuditActions.SessionRotated,
                AuditOutcome.Succeeded,
                SessionTarget(e.SessionId.Value))
            {
                // The generation number is deliberately not recorded: it is a refresh-token component and grows
                // into OTP-shaped digit strings that the sanitiser would (rightly) redact anyway.
                SubjectUserId = SessionUser(source)
            },

            SessionRevoked { Reason: SessionRevocationReason.ReuseDetected } e => new IdentityAuditDescriptor(
                IdentityAuditActions.SessionReuseDetected,
                AuditOutcome.Denied,
                SessionTarget(e.SessionId.Value))
            {
                SubjectUserId = SessionUser(source),
                ReasonCode = "Identity.SessionReuseDetected",
                Metadata = Meta(("revocationReason", e.Reason.ToString()))
            },

            SessionRevoked e => new IdentityAuditDescriptor(
                IdentityAuditActions.SessionRevoked,
                AuditOutcome.Succeeded,
                SessionTarget(e.SessionId.Value))
            {
                SubjectUserId = SessionUser(source),
                Metadata = Meta(("revocationReason", e.Reason.ToString()))
            },

            RoleGranted e => new IdentityAuditDescriptor(
                IdentityAuditActions.RoleGranted,
                AuditOutcome.Succeeded,
                new AuditTarget(IdentityAuditTargets.RoleAssignment, Id(e.AssignmentId.Value)))
            {
                SubjectUserId = e.UserId.Value,
                Metadata = Meta(
                    ("role", e.Role.ToString()),
                    ("scopeType", e.ScopeType.ToString()),
                    ("scopeId", e.ScopeId?.ToString()))
            },

            RoleRevoked e => new IdentityAuditDescriptor(
                IdentityAuditActions.RoleRevoked,
                AuditOutcome.Succeeded,
                new AuditTarget(IdentityAuditTargets.RoleAssignment, Id(e.AssignmentId.Value)))
            {
                SubjectUserId = e.UserId.Value,
                Metadata = Meta(
                    ("role", e.Role.ToString()),
                    ("scopeType", e.ScopeType.ToString()),
                    ("scopeId", e.ScopeId?.ToString()))
            },

            StaffInvited e => new IdentityAuditDescriptor(
                IdentityAuditActions.InvitationCreated,
                AuditOutcome.Succeeded,
                InvitationTarget(e.InvitationId.Value))
            {
                Metadata = Meta(
                    ("invitedBy", Id(e.InvitedByUserId.Value)),
                    ("channel", e.Channel.ToString()),
                    ("destinationFingerprint", fingerprinter.Fingerprint(e.Destination)),
                    ("role", e.Role.ToString()),
                    ("scopeType", e.ScopeType.ToString()),
                    ("scopeId", e.ScopeId?.ToString()))
            },

            StaffInvitationAccepted e => new IdentityAuditDescriptor(
                IdentityAuditActions.InvitationAccepted,
                AuditOutcome.Succeeded,
                InvitationTarget(e.InvitationId.Value))
            {
                SubjectUserId = e.UserId.Value,
                Metadata = Meta(
                    ("role", e.Role.ToString()),
                    ("scopeType", e.ScopeType.ToString()),
                    ("scopeId", e.ScopeId?.ToString()))
            },

            StaffInvitationRevoked e => new IdentityAuditDescriptor(
                IdentityAuditActions.InvitationRevoked,
                AuditOutcome.Succeeded,
                InvitationTarget(e.InvitationId.Value)),

            StaffBadgeIssued e => new IdentityAuditDescriptor(
                e.Reissued ? IdentityAuditActions.BadgeReissued : IdentityAuditActions.BadgeIssued,
                AuditOutcome.Succeeded,
                new AuditTarget(IdentityAuditTargets.StaffBadge, Id(e.BadgeId.Value)))
            {
                SubjectUserId = e.UserId.Value
            },

            StaffBadgeTitleChanged e => new IdentityAuditDescriptor(
                IdentityAuditActions.BadgeTitleChanged,
                AuditOutcome.Succeeded,
                new AuditTarget(IdentityAuditTargets.StaffBadge, Id(e.BadgeId.Value)))
            {
                SubjectUserId = e.UserId.Value
            },

            StaffBadgePhotoChanged e => new IdentityAuditDescriptor(
                IdentityAuditActions.BadgePhotoChanged,
                AuditOutcome.Succeeded,
                new AuditTarget(IdentityAuditTargets.StaffBadge, Id(e.BadgeId.Value)))
            {
                SubjectUserId = e.UserId.Value
            },

            CustomerRefCreated e => new IdentityAuditDescriptor(
                IdentityAuditActions.CustomerRefCreated,
                AuditOutcome.Succeeded,
                new AuditTarget(IdentityAuditTargets.CustomerRef, Id(e.CustomerRefId.Value)))
            {
                SubjectUserId = ParseUser(e.ExternalIdentityId),
                Metadata = Meta(("identityScope", e.IdentityScope.ToString()))
            },

            CustomerRefRefreshed e => new IdentityAuditDescriptor(
                IdentityAuditActions.CustomerRefRefreshed,
                AuditOutcome.Succeeded,
                new AuditTarget(IdentityAuditTargets.CustomerRef, Id(e.CustomerRefId.Value)))
            {
                SubjectUserId = ParseUser(e.ExternalIdentityId),
                Metadata = Meta(("identityScope", e.IdentityScope.ToString()))
            },

            // Never dump an unknown event's payload: it may contain personal data. Record its type only.
            _ => new IdentityAuditDescriptor(
                IdentityAuditActions.UnmappedDomainEvent,
                AuditOutcome.Succeeded,
                null)
            {
                Metadata = Meta(("eventType", @event.GetType().Name))
            }
        };
    }

    private static AuditTarget OtpTarget(Guid id) =>
        new(IdentityAuditTargets.OtpChallenge, Id(id));

    private static AuditTarget UserTarget(Guid id) =>
        new(IdentityAuditTargets.User, Id(id));

    private static AuditTarget SessionTarget(Guid id) =>
        new(IdentityAuditTargets.AuthSession, Id(id));

    private static AuditTarget InvitationTarget(Guid id) =>
        new(IdentityAuditTargets.StaffInvitation, Id(id));

    private static Guid? SessionUser(object? source) =>
        source is AuthSession session && !session.UserId.IsEmpty
            ? session.UserId.Value
            : null;

    private static Guid? ParseUser(string? value) =>
        Guid.TryParse(value, out var id)
            ? id
            : null;

    private static string Id(Guid id) =>
        id.ToString("D", CultureInfo.InvariantCulture);

    private static string Number(long value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static Dictionary<string, string> Meta(
        params (string Key, string? Value)[] values)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (key, value) in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                metadata[key] = value;
            }
        }

        return metadata;
    }
}
