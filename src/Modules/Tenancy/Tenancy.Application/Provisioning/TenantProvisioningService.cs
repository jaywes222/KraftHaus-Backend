using Common.Application.Persistence;
using Common.Domain.ResultMonad;
using Tenancy.Application.Abstractions;
using Tenancy.Domain.Common;
using Tenancy.Domain.Tenants;

namespace Tenancy.Application.Provisioning;

public sealed class TenantProvisioningService(
    ITenantRegistry registry,
    ITenantDatabaseConnectionFactory connectionFactory,
    IDatabaseInitializer databaseInitializer) : ITenantProvisioner
{
    public async Task<Result> ProvisionAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var descriptor = await registry.FindByIdAsync(
            new TenantId(tenantId),
            cancellationToken);

        if (descriptor is null)
        {
            return Error.NotFound(nameof(TenantId), tenantId);
        }

        return await Result.CreateAsync(async () =>
        {
            var connectionString = connectionFactory.Create(
                descriptor.TenantId,
                descriptor.DatabaseName);

            await databaseInitializer.InitializeTenantAsync(
                descriptor.TenantId.Value,
                descriptor.DatabaseName,
                connectionString,
                cancellationToken);
        });
    }

    public async Task<Result> InitializeAllAsync(
        CancellationToken cancellationToken)
    {
        await databaseInitializer.InitializePlatformAsync(
            cancellationToken);

        var tenants = await registry.GetAllAsync(
            cancellationToken);

        foreach (var tenant in tenants.Where(
            x => x.Status is not TenantStatus.Disabled &&
                 x.IdentityScope == IdentityScope.Tenant))
        {
            var connectionString = connectionFactory.Create(
                tenant.TenantId,
                tenant.DatabaseName);

            await databaseInitializer.InitializeTenantAsync(
                tenant.TenantId.Value,
                tenant.DatabaseName,
                connectionString,
                cancellationToken);
        }

        return Result.Success;
    }
}
