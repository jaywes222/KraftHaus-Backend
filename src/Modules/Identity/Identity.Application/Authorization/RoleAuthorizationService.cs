using Common.Domain.StronglyTypedIds;
using Identity.Domain.Roles;

namespace Identity.Application.Authorization;

public sealed class RoleAuthorizationService(
    IRoleAssignmentRepository assignments)
{
    public async Task<bool> HasRoleAsync(
        ApplicationUserId userId,
        IdentityRole role,
        RoleScopeType scopeType,
        Guid? scopeId,
        CancellationToken cancellationToken)
    {
        var activeAssignments =
            await assignments.GetActiveForUserAsync(
                userId,
                cancellationToken);

        return activeAssignments.Any(x =>
            x.UserId == userId &&
            x.Role == role &&
            x.ScopeType == scopeType &&
            x.ScopeId == scopeId &&
            x.Status == RoleAssignmentStatus.Active);
    }

    public async Task<bool> HasAnyRoleAsync(
        ApplicationUserId userId,
        IEnumerable<IdentityRole> roles,
        RoleScopeType scopeType,
        Guid? scopeId,
        CancellationToken cancellationToken)
    {
        var allowedRoles = roles.ToHashSet();

        var activeAssignments =
            await assignments.GetActiveForUserAsync(
                userId,
                cancellationToken);

        return activeAssignments.Any(x =>
            x.UserId == userId &&
            allowedRoles.Contains(x.Role) &&
            x.ScopeType == scopeType &&
            x.ScopeId == scopeId &&
            x.Status == RoleAssignmentStatus.Active);
    }
}
