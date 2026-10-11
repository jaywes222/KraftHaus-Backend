using Common.Application.Persistence;
using Common.Domain.ResultMonad;
using Common.Domain.Tenancy;
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

        return descriptor is null
            ? (Result)Error.NotFound(nameof(TenantId), tenantId)
            : await Result.CreateAsync(async () =>
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

        // Every tenant owns a tenant database (events, tickets, payments, ...), including the internal KRAFTHAUS
        // tenant whose *identities* live in the platform database (IdentityScope.Platform, Spec 08 §5.1).
        foreach (var tenant in tenants.Where(
            x => x.Status is not TenantStatus.Disabled &&
                 !string.IsNullOrWhiteSpace(x.DatabaseName)))
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
