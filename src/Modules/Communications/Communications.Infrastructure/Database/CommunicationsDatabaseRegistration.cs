using Common.Application.Persistence;
using Common.Application.Tenancy;
using Common.Infrastructure.Persistence.Auditing;
using EntityFramework.Exceptions.PostgreSQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using Communications.Infrastructure.Auditing;
using Communications.Infrastructure.Persistence;

namespace Communications.Infrastructure.Database;

public static class CommunicationsDatabaseRegistration
{
    public const string ModuleName = "Communications";

    /// <summary>
    ///     Request-time <see cref="CommunicationsDbContext" /> bound to the resolved tenant's database, plus the migration
    ///     descriptor (tenant scope) so provisioning applies the Communications schema to every tenant database.
    /// </summary>
    public static IServiceCollection AddCommunicationsPersistence(this IServiceCollection services)
    {
        services.AddScoped<CommunicationsAuditSaveChangesInterceptor>();

        services.AddDbContext<CommunicationsDbContext>((sp, options) =>
        {
            var tenantDatabase = sp.GetRequiredService<ITenantDatabaseConnection>();

            options
                .UseNpgsql(tenantDatabase.GetConnectionString(), Configure)
                .UseExceptionProcessor()
                .AddInterceptors(
                    // CreatedOn/CreatedBy/LastModifiedOn/LastModifiedBy on every auditable row.
                    sp.GetRequiredService<ApplyAuditingInterceptor>(),

                    // communications.audit_log rows in the same transaction as the change.
                    sp.GetRequiredService<CommunicationsAuditSaveChangesInterceptor>());
        });

        return services.AddCommunicationsMigrations();
    }

    /// <summary>
    ///     Only the migration descriptor: used by hosts that provision tenant databases without serving requests
    ///     (the Migrator).
    /// </summary>
    public static IServiceCollection AddCommunicationsMigrations(this IServiceCollection services)
    {
        services.AddSingleton(
            new ModuleDatabaseDescriptor(
                ModuleName,
                DatabaseScope.Tenant,
                connectionString => new CommunicationsDbContext(
                    new DbContextOptionsBuilder<CommunicationsDbContext>()
                        .UseNpgsql(connectionString, Configure)
                        .Options)));

        return services;
    }

    internal static void Configure(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MigrationsHistoryTable(CommunicationsDbContext.MigrationsHistoryTable, CommunicationsDbContext.Schema);
}
