using Common.Application.Persistence;
using Common.Application.Tenancy;
using Common.Infrastructure.Persistence.Auditing;
using EntityFramework.Exceptions.PostgreSQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using Commerce.Infrastructure.Auditing;
using Commerce.Infrastructure.Persistence;

namespace Commerce.Infrastructure.Database;

public static class CommerceDatabaseRegistration
{
    public const string ModuleName = "Commerce";

    /// <summary>
    ///     Request-time <see cref="CommerceDbContext" /> bound to the resolved tenant's database, plus the migration
    ///     descriptor (tenant scope) so provisioning applies the Commerce schema to every tenant database.
    /// </summary>
    public static IServiceCollection AddCommercePersistence(this IServiceCollection services)
    {
        services.AddScoped<CommerceAuditSaveChangesInterceptor>();

        services.AddDbContext<CommerceDbContext>((sp, options) =>
        {
            var tenantDatabase = sp.GetRequiredService<ITenantDatabaseConnection>();

            options
                .UseNpgsql(tenantDatabase.GetConnectionString(), Configure)
                .UseExceptionProcessor()
                .AddInterceptors(
                    // CreatedOn/CreatedBy/LastModifiedOn/LastModifiedBy on every auditable row.
                    sp.GetRequiredService<ApplyAuditingInterceptor>(),

                    // commerce.audit_log rows in the same transaction as the change.
                    sp.GetRequiredService<CommerceAuditSaveChangesInterceptor>());
        });

        return services.AddCommerceMigrations();
    }

    /// <summary>
    ///     Only the migration descriptor: used by hosts that provision tenant databases without serving requests
    ///     (the Migrator).
    /// </summary>
    public static IServiceCollection AddCommerceMigrations(this IServiceCollection services)
    {
        services.AddSingleton(
            new ModuleDatabaseDescriptor(
                ModuleName,
                DatabaseScope.Tenant,
                connectionString => new CommerceDbContext(
                    new DbContextOptionsBuilder<CommerceDbContext>()
                        .UseNpgsql(connectionString, Configure)
                        .Options)));

        return services;
    }

    internal static void Configure(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MigrationsHistoryTable(CommerceDbContext.MigrationsHistoryTable, CommerceDbContext.Schema);
}
