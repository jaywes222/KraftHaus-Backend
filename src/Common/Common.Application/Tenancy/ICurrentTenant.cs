using Common.Domain.Tenancy;

namespace Common.Application.Tenancy;

/// <summary>
///     Read-only view of the tenant resolved for the current request (by host name, validated against the token's
///     <c>tid</c>). Implemented by the Tenancy module so other modules can depend on it without referencing Tenancy.
/// </summary>
public interface ICurrentTenant
{
    bool IsResolved { get; }

    /// <summary>The resolved tenant. Only meaningful when <see cref="IsResolved" /> is true.</summary>
    TenantId TenantId { get; }

    /// <summary>Where the resolved tenant keeps its customer identities. Only meaningful when resolved.</summary>
    IdentityScope IdentityScope { get; }
}
