 using Common.Domain.Tenancy;

namespace Identity.Domain.Users;

/// <summary>
///     The identity directory a user lives in: the shared platform directory, or one tenant's own directory
///     (Spec 08 §5). Phone numbers and e-mail addresses are unique per realm, never globally.
/// </summary>
public readonly record struct IdentityRealm(
    IdentityScope Scope,
    TenantId? TenantId)
{
    public static IdentityRealm Platform { get; } = new(IdentityScope.Platform, null);

    public static IdentityRealm ForTenant(TenantId tenantId) =>
        new(IdentityScope.Tenant, tenantId);

    /// <summary>The realm a tenant host signs users into, based on the tenant's identity scope.</summary>
    public static IdentityRealm ForHost(
        TenantId tenantId,
        IdentityScope tenantIdentityScope) =>
        tenantIdentityScope == IdentityScope.Platform
            ? Platform
            : ForTenant(tenantId);
}
