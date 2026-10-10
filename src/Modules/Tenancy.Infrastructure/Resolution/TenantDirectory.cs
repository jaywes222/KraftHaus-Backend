using Common.Application.Tenancy;
using Tenancy.Application.Abstractions;
using Tenancy.Domain.Tenants;

namespace Tenancy.Infrastructure.Resolution;

/// <summary>Adapts the tenant registry to the cross-module <see cref="ITenantDirectory" /> view.</summary>
internal sealed class TenantDirectory(ITenantRegistry registry) : ITenantDirectory
{
    public async Task<TenantHostInfo?> FindByHostAsync(string hostName, CancellationToken cancellationToken)
    {
        var host = HostName.Create(hostName);

        if (host.IsFailure)
        {
            return null;
        }

        var tenant = await registry.FindByHostAsync(host.Value!, cancellationToken);

        return tenant is null ? null : new TenantHostInfo(tenant.TenantId, tenant.IdentityScope);
    }
}
