using Microsoft.EntityFrameworkCore;
using Tenancy.Application.Abstractions;
using Tenancy.Domain.Common;
using Tenancy.Domain.Tenants;
using Tenancy.Infrastructure.Persistence;

namespace Tenancy.Infrastructure.Resolution;

public sealed class TenantRegistry(IDbContextFactory<TenancyDbContext> factory) : ITenantRegistry
{
    public async Task<TenantDescriptor?> FindByHostAsync(HostName hostName, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.TenantHosts
            .AsNoTracking()
            .Where(x => x.HostName == hostName && x.Enabled)
            .Join(db.Tenants.AsNoTracking(), host => host.TenantId, tenant => tenant.Id, (host, tenant) => new TenantDescriptor(
                tenant.Id,
                tenant.Slug,
                tenant.IdentityScope,
                tenant.Status,
                tenant.DatabaseName!))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<TenantDescriptor?> FindByIdAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.Tenants
            .AsNoTracking()
            .Where(x => x.Id == tenantId)
            .Select(x => new TenantDescriptor(x.Id, x.Slug, x.IdentityScope, x.Status, x.DatabaseName!))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<TenantDescriptor>> GetAllAsync(CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.Tenants
            .AsNoTracking()
            .OrderBy(x => x.Slug)
            .Select(x => new TenantDescriptor(x.Id, x.Slug, x.IdentityScope, x.Status, x.DatabaseName!))
            .ToArrayAsync(cancellationToken);
    }

    public async Task<long> GetVersionAsync(CancellationToken cancellationToken)
    {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        return await db.Tenants.AsNoTracking().Select(x => (long?)x.Version).MaxAsync(cancellationToken) ?? 0;
    }
}
