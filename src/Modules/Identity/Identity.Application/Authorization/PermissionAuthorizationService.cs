using Common.Domain.StronglyTypedIds;
using Identity.Application.Abstractions;
using Identity.Domain.Authorization;
using Identity.Domain.Roles;

namespace Identity.Application.Authorization;

public sealed class PermissionAuthorizationService(
    IRoleAssignmentRepository roleAssignments)
    : IPermissionAuthorizationService
{
    public async Task<bool> HasPermissionAsync(
        ApplicationUserId userId,
        string permission,
        PermissionScope scope,
        CancellationToken cancellationToken)
    {
        if (!IdentityPermission.All.Contains(permission))
        {
            return false;
        }

        var assignments =
            await roleAssignments.GetActiveForUserAsync(
                userId,
                cancellationToken);

        foreach (var assignment in assignments)
        {
            if (!MatchesScope(
                    assignment,
                    scope))
            {
                continue;
            }

            if (IdentityPermissionMatrix.HasPermission(
                    assignment.Role,
                    permission))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesScope(
        RoleAssignment assignment,
        PermissionScope requested)
    {
        // Super Admin is granted at platform scope and applies everywhere (Spec 08 §6.2).
        if (assignment.Role == IdentityRole.SuperAdmin &&
            assignment.ScopeType == RoleScopeType.Platform)
        {
            return true;
        }

        if (assignment.ScopeType !=
            requested.ScopeType)
        {
            return false;
        }

        return assignment.ScopeId ==
            requested.ScopeId;
    }
}
