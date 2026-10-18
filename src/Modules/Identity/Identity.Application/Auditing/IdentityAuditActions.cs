namespace Identity.Application.Auditing;

/// <summary>
///     Catalogue of audited Identity actions (WP-08.1). Names are stable, dotted, lower-case and never localised:
///     dashboards, alerts and retention rules key off them, so treat a rename like a breaking API change.
///     <para>
///         State-change actions are projected from domain events in the same transaction as the change.
///         <c>*_rejected</c> actions record a refused request, including refusals that changed no state.
///     </para>
/// </summary>
public static class IdentityAuditActions
{
    public const string Module = "identity";

    // OTP challenge lifecycle (domain events).
    public const string OtpIssued = "identity.otp.issued";
    public const string OtpVerified = "identity.otp.verified";
    public const string OtpVerificationFailed = "identity.otp.verification_failed";
    public const string OtpLocked = "identity.otp.locked";
    public const string OtpExpired = "identity.otp.expired";
    public const string OtpSuperseded = "identity.otp.superseded";

    // Users and consent (domain events).
    public const string UserRegistered = "identity.user.registered";
    public const string UserSignedIn = "identity.user.signed_in";
    public const string UserProfileChanged = "identity.user.profile_changed";
    public const string UserDisabled = "identity.user.disabled";
    public const string UserEnabled = "identity.user.enabled";
    public const string ConsentRecorded = "identity.consent.recorded";

    // Sessions and refresh tokens (domain events).
    public const string SessionStarted = "identity.session.started";
    public const string SessionRotated = "identity.session.rotated";
    public const string SessionRevoked = "identity.session.revoked";
    public const string SessionReuseDetected = "identity.session.reuse_detected";

    // Roles and staff invitations (domain events).
    public const string RoleGranted = "identity.role.granted";
    public const string RoleRevoked = "identity.role.revoked";
    public const string InvitationCreated = "identity.invitation.created";
    public const string InvitationAccepted = "identity.invitation.accepted";
    public const string InvitationRevoked = "identity.invitation.revoked";

    // Staff badges (domain events).
    public const string BadgeIssued = "identity.badge.issued";
    public const string BadgeReissued = "identity.badge.reissued";
    public const string BadgeTitleChanged = "identity.badge.title_changed";
    public const string BadgePhotoChanged = "identity.badge.photo_changed";

    // Customer references (domain events).
    public const string CustomerRefCreated = "identity.customer_ref.created";
    public const string CustomerRefRefreshed = "identity.customer_ref.refreshed";

    // Refused requests (recorded by the application services / authorization handler).
    public const string OtpRequestRejected = "identity.otp.request_rejected";
    public const string SignInRejected = "identity.sign_in.rejected";
    public const string SessionStartRejected = "identity.session.start_rejected";
    public const string SessionRefreshRejected = "identity.session.refresh_rejected";
    public const string SessionRotationRejected = "identity.session.rotation_rejected";
    public const string SessionRevokeRejected = "identity.session.revoke_rejected";
    public const string InvitationAcceptRejected = "identity.invitation.accept_rejected";
    public const string InvitationCreateRejected = "identity.invitation.create_rejected";
    public const string InvitationRevokeRejected = "identity.invitation.revoke_rejected";
    public const string RoleGrantRejected = "identity.role.grant_rejected";
    public const string RoleRevokeRejected = "identity.role.revoke_rejected";
    public const string AuthorizationDenied = "identity.authorization.denied";

    /// <summary>Fallback for a domain event without an explicit mapping. A unit test keeps this unused.</summary>
    public const string UnmappedDomainEvent = "identity.domain_event.unmapped";
}

/// <summary>Target type names used in <c>AuditTarget.Type</c> for Identity entries.</summary>
public static class IdentityAuditTargets
{
    public const string OtpChallenge = "otp_challenge";
    public const string AuthSession = "auth_session";
    public const string User = "application_user";
    public const string RoleAssignment = "role_assignment";
    public const string StaffInvitation = "staff_invitation";
    public const string CustomerRef = "customer_ref";
    public const string StaffBadge = "staff_badge";
    public const string Permission = "permission";
}
