using Common.Application.Tenancy;
using Common.Application.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Tenancy.Application.Abstractions;
using Tenancy.Application.Provisioning;
using Tenancy.Infrastructure.Database;
using Tenancy.Infrastructure.Resolution;
using Tenancy.Infrastructure.Seeding;

namespace Tenancy.Infrastructure;

public static class Setup
{
    public static IServiceCollection AddTenancy(this IServiceCollection services)
    {
        services.AddMemoryCache();

        services.AddTenancyPersistence();

        services.AddSingleton<ITenantRegistry, TenantRegistry>();
        services.AddScoped<ITenantResolutionService, TenantResolutionService>();
        services.AddScoped<ITenantProvisioner, TenantProvisioningService>();

        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(
            sp => sp.GetRequiredService<TenantContext>());

        // Exposes the resolved tenant to other modules (e.g. audit stamping) without a Tenancy reference.
        services.AddScoped<ICurrentTenant, CurrentTenant>();
        services.AddScoped<ITenantDirectory, TenantDirectory>();

        services.AddScoped<ITenantScopeFactory, TenantScopeFactory>();

        services.AddSingleton<
            ITenantDatabaseConnectionFactory,
            TenantDatabaseConnectionFactory>();

        // Infrastructure-only provider; deliberately not exposed through Application.
        services.AddSingleton<TenantDataSourceProvider>();

        services.AddScoped<IDatabaseSeeder, TenancySeeder>();

        return services;
    }
}
