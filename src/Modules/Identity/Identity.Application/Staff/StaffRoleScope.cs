using Common.Domain.ResultMonad;
using Common.Domain.Tenancy;
using Identity.Domain.Common;
using Identity.Domain.Roles;

namespace Identity.Application.Staff;

/// <summary>
///     Where a staff role granted on a tenant host applies. Tenant roles apply to the host's tenant (never one named by
///     the caller); Kraft roles need the Kraft id. Super Admin is platform-wide and cannot be granted from a tenant.
/// </summary>
public static class StaffRoleScope
{
    public static Result<(RoleScopeType ScopeType, Guid? ScopeId)> Resolve(
        IdentityRole role,
        TenantId tenantId,
        Guid? kraftId)
    {
        switch (role)
        {
            case IdentityRole.TenantAdmin:
            case IdentityRole.Finance:
            case IdentityRole.Support:
                return kraftId is null
                    ? (RoleScopeType.Tenant, tenantId.Value)
                    : IdentityErrors.Invalid(
                        "KraftId",
                        $"Role '{role}' applies to the whole tenant; do not send a kraftId.");

            case IdentityRole.KraftOwner:
            case IdentityRole.KraftStaff:
                return kraftId is { } id && id != Guid.Empty
                    ? (RoleScopeType.Kraft, id)
                    : IdentityErrors.Required("KraftId", $"Role '{role}' requires a kraftId.");

            case IdentityRole.SuperAdmin:
                return IdentityErrors.Forbidden(
                    "RoleNotGrantable",
                    "Super Admin is platform-wide and cannot be granted from a tenant.");

            default:
                return IdentityErrors.Invalid("Role", "Unknown role.");
        }
    }

    /// <summary>
    ///     True when an existing assignment belongs to this tenant: tenant roles carry the tenant id; Kraft roles are
    ///     checked through the user, who must live in the host's realm.
    /// </summary>
    public static bool BelongsToTenant(RoleAssignment assignment, TenantId tenantId)
    {
        ArgumentNullException.ThrowIfNull(assignment);

        return assignment.ScopeType switch
        {
            RoleScopeType.Tenant => assignment.ScopeId == tenantId.Value,
            RoleScopeType.Kraft => true,
            _ => false
        };
    }
}
