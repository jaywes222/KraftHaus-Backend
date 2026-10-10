using Identity.Domain.Roles;

namespace Identity.Domain.Authorization;

public readonly record struct PermissionScope(
    RoleScopeType ScopeType,
    Guid? ScopeId)
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
        Guid kraftId)
    {
        return new PermissionScope(
            RoleScopeType.Kraft,
            kraftId);
    }
}
