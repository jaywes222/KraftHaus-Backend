using Common.Application.Tenancy;
using Common.Domain.Tenancy;

namespace Tenancy.Infrastructure.Resolution;

/// <summary>
///     Adapts the request-scoped <see cref="TenantContext" /> to the cross-module <see cref="ICurrentTenant" /> view.
/// </summary>
internal sealed class CurrentTenant(TenantContext context) : ICurrentTenant
{
    public bool IsResolved => context.IsResolved;

    public TenantId TenantId => context.TenantId;

    public IdentityScope IdentityScope => context.IdentityScope;
}
