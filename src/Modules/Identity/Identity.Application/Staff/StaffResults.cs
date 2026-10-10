using Common.Domain.StronglyTypedIds;
using Identity.Domain.Common;
using Identity.Domain.Invitations;
using Identity.Domain.Roles;

namespace Identity.Application.Staff;

public sealed record StaffInvitationResult(
    StaffInvitationId InvitationId,
    InvitationChannel Channel,
    string Destination,
    IdentityRole Role,
    RoleScopeType ScopeType,
    Guid? ScopeId,
    StaffInvitationStatus Status,
    bool Expired,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt)
{
    public static StaffInvitationResult From(StaffInvitation invitation, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(invitation);

        return new StaffInvitationResult(
            invitation.Id,
            invitation.Channel,
            invitation.Destination,
            invitation.Role,
            invitation.ScopeType,
            invitation.ScopeId,
            invitation.Status,
            invitation.IsExpired(now),
            invitation.CreatedAt,
            invitation.ExpiresAt);
    }
}

public sealed record RoleAssignmentResult(
    RoleAssignmentId AssignmentId,
    ApplicationUserId UserId,
    IdentityRole Role,
    RoleScopeType ScopeType,
    Guid? ScopeId,
    DateTimeOffset GrantedAt)
{
    public static RoleAssignmentResult From(RoleAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        return new RoleAssignmentResult(
            assignment.Id,
            assignment.UserId,
            assignment.Role,
            assignment.ScopeType,
            assignment.ScopeId,
            assignment.GrantedAt);
    }
}
