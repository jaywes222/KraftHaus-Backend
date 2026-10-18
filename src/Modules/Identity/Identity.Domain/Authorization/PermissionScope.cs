using Identity.Domain.Roles;

namespace Identity.Domain.Authorization;

/// <param name="TenantId">
///     For a Kraft scope: the tenant the Kraft belongs to (the host's tenant). Tenant-wide roles of that tenant apply to
///     its Krafts (a Tenant Admin can manage any Kraft); Kraft roles apply only to their own Kraft (BR-026).
/// </param>
public readonly record struct PermissionScope(
    RoleScopeType ScopeType,
    Guid? ScopeId,
    Guid? TenantId = null)
{
    public static PermissionScope Platform()
    {
        return new PermissionScope(
            RoleScopeType.Platform,
            null);
    }

    public static PermissionScope Tenant(
        Guid tenantId)
    {
        return new PermissionScope(
            RoleScopeType.Tenant,
            tenantId);
    }

    public static PermissionScope Kraft(
        Guid kraftId,
        Guid? tenantId = null)
    {
        return new PermissionScope(
            RoleScopeType.Kraft,
            kraftId,
            tenantId);
    }
}
