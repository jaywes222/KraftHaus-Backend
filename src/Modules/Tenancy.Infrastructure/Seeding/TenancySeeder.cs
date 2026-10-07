using Common.Application.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Tenancy.Application.Abstractions;
using Tenancy.Domain.Common;
using Tenancy.Domain.Tenants;
using Tenancy.Infrastructure.Database;
using Tenancy.Infrastructure.Persistence;

namespace Tenancy.Infrastructure.Seeding;

public sealed class TenancySeeder(
    TenancyDbContext db,
    IDatabaseInitializer databaseInitializer,
    ITenantDatabaseConnectionFactory connectionFactory,
    IHostEnvironment environment)
    : IDatabaseSeeder
{
    private const int SeederPriority = 10;

    private const string DevelopmentSlug = "DEV";
    private const string DevelopmentName = "Development";
    private const string DevelopmentDatabaseName = "krafthaus_dev";
    private const string DevelopmentHost = "localhost";

    public int Priority => SeederPriority;

    public async Task SeedAsync(
        CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            return;
        }

        var slug = TenantSlug.Create(DevelopmentSlug);

        if (slug.IsFailure)
        {
            throw new InvalidOperationException(
                slug.Error!.Description);
        }

        var tenant = await db.Tenants
            .Include(x => x.Hosts)
            .SingleOrDefaultAsync(
                x => x.Slug == slug.Value,
                cancellationToken);

        if (tenant is null)
        {
            tenant = await CreateDevelopmentTenantAsync(
                slug.Value!,
                cancellationToken);
        }

        var hostName = HostName.Create(DevelopmentHost);

        if (hostName.IsFailure)
        {
            throw new InvalidOperationException(
                hostName.Error!.Description);
        }

        if (!tenant.Hosts.Any(
            x => x.HostName == hostName.Value))
        {
            var host = TenantHost.Create(
                new TenantHostId(Guid.CreateVersion7()),
                tenant.Id,
                hostName.Value!,
                HostSurface.Client |
                HostSurface.Admin |
                HostSurface.Ops |
                HostSurface.Display,
                isPrimary: true);

            var result = tenant.AddHost(host);

            if (result.IsFailure)
            {
                throw new InvalidOperationException(
                    result.Error!.Description);
            }

            await db.SaveChangesAsync(
                cancellationToken);
        }

        if (tenant.Status != TenantStatus.Active)
        {
            var connectionString =
                connectionFactory.Create(
                    tenant.Id,
                    DevelopmentDatabaseName);

            await databaseInitializer.InitializeTenantAsync(
                tenant.Id.Value,
                DevelopmentDatabaseName,
                connectionString,
                cancellationToken);

            var provisionResult =
                tenant.MarkProvisioned(
                    DevelopmentDatabaseName);

            if (provisionResult.IsFailure)
            {
                throw new InvalidOperationException(
                    provisionResult.Error!.Description);
            }

            await db.SaveChangesAsync(
                cancellationToken);
        }
    }

    private async Task<Tenant> CreateDevelopmentTenantAsync(
        TenantSlug slug,
        CancellationToken cancellationToken)
    {
        var tenantResult = Tenant.Create(
            new TenantId(Guid.CreateVersion7()),
            slug,
            DevelopmentName,
            IdentityScope.Tenant);

        if (tenantResult.IsFailure)
        {
            throw new InvalidOperationException(
                tenantResult.Error!.Description);
        }

        var tenant = tenantResult.Value!;

        db.Tenants.Add(tenant);

        await db.SaveChangesAsync(
            cancellationToken);

        return tenant;
    }
}
