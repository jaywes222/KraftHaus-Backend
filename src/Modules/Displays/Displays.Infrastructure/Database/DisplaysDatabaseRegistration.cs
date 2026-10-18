using Common.Application.Persistence;
using Common.Application.Tenancy;
using Common.Infrastructure.Persistence.Auditing;
using EntityFramework.Exceptions.PostgreSQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using Displays.Infrastructure.Auditing;
using Displays.Infrastructure.Persistence;

namespace Displays.Infrastructure.Database;

public static class DisplaysDatabaseRegistration
{
    public const string ModuleName = "Displays";

    /// <summary>
    ///     Request-time <see cref="DisplaysDbContext" /> bound to the resolved tenant's database, plus the migration
    ///     descriptor (tenant scope) so provisioning applies the Displays schema to every tenant database.
    /// </summary>
    public static IServiceCollection AddDisplaysPersistence(this IServiceCollection services)
    {
        services.AddScoped<DisplaysAuditSaveChangesInterceptor>();

        services.AddDbContext<DisplaysDbContext>((sp, options) =>
        {
            var tenantDatabase = sp.GetRequiredService<ITenantDatabaseConnection>();

            options
                .UseNpgsql(tenantDatabase.GetConnectionString(), Configure)
                .UseExceptionProcessor()
                .AddInterceptors(
                    // CreatedOn/CreatedBy/LastModifiedOn/LastModifiedBy on every auditable row.
                    sp.GetRequiredService<ApplyAuditingInterceptor>(),

                    // displays.audit_log rows in the same transaction as the change.
                    sp.GetRequiredService<DisplaysAuditSaveChangesInterceptor>());
        });

        return services.AddDisplaysMigrations();
    }

    /// <summary>
    ///     Only the migration descriptor: used by hosts that provision tenant databases without serving requests
    ///     (the Migrator).
    /// </summary>
    public static IServiceCollection AddDisplaysMigrations(this IServiceCollection services)
    {
        services.AddSingleton(
            new ModuleDatabaseDescriptor(
                ModuleName,
                DatabaseScope.Tenant,
                connectionString => new DisplaysDbContext(
                    new DbContextOptionsBuilder<DisplaysDbContext>()
                        .UseNpgsql(connectionString, Configure)
                        .Options)));

        return services;
    }

    internal static void Configure(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MigrationsHistoryTable(DisplaysDbContext.MigrationsHistoryTable, DisplaysDbContext.Schema);
}
