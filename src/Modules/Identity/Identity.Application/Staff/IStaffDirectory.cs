using Common.Domain.StronglyTypedIds;
using Identity.Domain.Common;
using Identity.Domain.Roles;
using Identity.Domain.Users;

namespace Identity.Application.Staff;

/// <summary>
///     Read side of the staff list (UF-022): every active role assignment held by a user of one identity realm, with
///     that user's profile. Scope filtering (which tenant or Kraft the role applies to) is the caller's job.
/// </summary>
public interface IStaffDirectory
{
    Task<IReadOnlyList<StaffAssignmentRow>> ListActiveAssignmentsAsync(
        IdentityRealm realm,
        CancellationToken cancellationToken);
}

/// <summary>One active role assignment joined to its user. Phone and e-mail are the stored (normalised) values.</summary>
public sealed record StaffAssignmentRow(
    ApplicationUserId UserId,
    string DisplayName,
    string? Phone,
    string? Email,
    UserStatus UserStatus,
    DateTimeOffset? LastSignedInAt,
    RoleAssignmentId AssignmentId,
    IdentityRole Role,
    RoleScopeType ScopeType,
    Guid? ScopeId,
    DateTimeOffset GrantedAt);
