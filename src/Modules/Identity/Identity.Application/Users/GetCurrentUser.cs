using Common.Application.Tenancy;
using Common.Domain.ResultMonad;
using Common.Domain.StronglyTypedIds;
using Common.Domain.Tenancy;
using Identity.Application.Abstractions;
using Identity.Application.Authorization;
using Identity.Application.Staff;
using Identity.Domain.Authorization;
using Identity.Domain.Common;
using Identity.Domain.Roles;
using Identity.Domain.Users;

namespace Identity.Application.Users;

public sealed record CurrentUserResult(
    ApplicationUserId UserId,
    string DisplayName,
    string? Phone,
    string? Email,
    UserStatus Status,
    IdentityScope IdentityScope,
    TenantId? TenantId,
    string? SessionId,
    IReadOnlyList<RoleAssignmentResult> Roles,
    IReadOnlyList<string> TenantPermissions);

/// <summary>
///     "Who am I" for the signed-in caller on this host: profile, role assignments and the permissions they hold for
///     the host's tenant (Kraft-level permissions are per Kraft; see <see cref="CurrentUserResult.Roles" />).
/// </summary>
public sealed class GetCurrentUserService(
    IIdentityUserRepository users,
    IRoleAssignmentRepository assignments,
    ICurrentTenant currentTenant)
{
    public async Task<Result<CurrentUserResult>> ExecuteAsync(
        ApplicationUserId userId,
        string? sessionId,
        CancellationToken cancellationToken)
    {
        if (!currentTenant.IsResolved)
        {
            return IdentityErrors.InvalidState("TenantUnresolved", "This request requires a tenant host.");
        }

        var user = await users.FindAsync(userId, cancellationToken);
        var realm = IdentityRealm.ForHost(currentTenant.TenantId, currentTenant.IdentityScope);

        // A token from another tenant's directory is not a user of this host.
        if (user is null || !user.IsIn(realm))
        {
            return IdentityErrors.Forbidden("TenantMismatch", "This account does not belong to this site.");
        }

        var active = await assignments.GetActiveForUserAsync(userId, cancellationToken);

        var tenantPermissions = active
            .Where(x =>
                x.Role == IdentityRole.SuperAdmin ||
                (x.ScopeType == RoleScopeType.Tenant && x.ScopeId == currentTenant.TenantId.Value))
            .SelectMany(x => IdentityPermissionMatrix.GetPermissions(x.Role))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        return new CurrentUserResult(
            user.Id,
            user.DisplayName,
            user.Phone?.Value,
            user.Email?.Value,
            user.Status,
            user.Scope,
            user.TenantId,
            sessionId,
            [.. active.OrderBy(x => x.GrantedAt).Select(RoleAssignmentResult.From)],
            tenantPermissions);
    }
}
