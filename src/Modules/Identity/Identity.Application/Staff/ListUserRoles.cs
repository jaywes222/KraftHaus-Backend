using Common.Application.Tenancy;
using Common.Domain.ResultMonad;
using Common.Domain.StronglyTypedIds;
using Identity.Application.Abstractions;
using Identity.Application.Authorization;
using Identity.Domain.Common;
using Identity.Domain.Users;

namespace Identity.Application.Staff;

public sealed class ListUserRolesService(
    IIdentityUserRepository users,
    IRoleAssignmentRepository assignments,
    ICurrentTenant currentTenant)
{
    public async Task<Result<IReadOnlyList<RoleAssignmentResult>>> ExecuteAsync(
        ApplicationUserId userId,
        CancellationToken cancellationToken)
    {
        if (!currentTenant.IsResolved)
        {
            return IdentityErrors.InvalidState("TenantUnresolved", "Listing roles requires a tenant host.");
        }

        var realm = IdentityRealm.ForHost(currentTenant.TenantId, currentTenant.IdentityScope);
        var user = await users.FindAsync(userId, cancellationToken);

        if (user is null || !user.IsIn(realm))
        {
            return Error.NotFound(nameof(userId), userId.Value);
        }

        var active = await assignments.GetActiveForUserAsync(userId, cancellationToken);

        return Result<IReadOnlyList<RoleAssignmentResult>>.Success(
        [
            .. active
                .Where(x => StaffRoleScope.BelongsToTenant(x, currentTenant.TenantId))
                .OrderBy(x => x.GrantedAt)
                .Select(RoleAssignmentResult.From)
        ]);
    }
}
