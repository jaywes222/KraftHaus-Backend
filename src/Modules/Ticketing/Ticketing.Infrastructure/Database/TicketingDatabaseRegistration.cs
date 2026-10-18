using Common.Application.Persistence;
using Common.Application.Tenancy;
using Common.Infrastructure.Persistence.Auditing;
using EntityFramework.Exceptions.PostgreSQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using Ticketing.Infrastructure.Auditing;
using Ticketing.Infrastructure.Persistence;

namespace Ticketing.Infrastructure.Database;

public static class TicketingDatabaseRegistration
{
    public const string ModuleName = "Ticketing";

    /// <summary>
    ///     Request-time <see cref="TicketingDbContext" /> bound to the resolved tenant's database, plus the migration
    ///     descriptor (tenant scope) so provisioning applies the Ticketing schema to every tenant database.
    /// </summary>
    public static IServiceCollection AddTicketingPersistence(this IServiceCollection services)
    {
        services.AddScoped<TicketingAuditSaveChangesInterceptor>();

        services.AddDbContext<TicketingDbContext>((sp, options) =>
        {
            var tenantDatabase = sp.GetRequiredService<ITenantDatabaseConnection>();

            options
                .UseNpgsql(tenantDatabase.GetConnectionString(), Configure)
                .UseExceptionProcessor()
                .AddInterceptors(
                    // CreatedOn/CreatedBy/LastModifiedOn/LastModifiedBy on every auditable row.
                    sp.GetRequiredService<ApplyAuditingInterceptor>(),

                    // ticketing.audit_log rows in the same transaction as the change.
                    sp.GetRequiredService<TicketingAuditSaveChangesInterceptor>());
        });

        return services.AddTicketingMigrations();
    }

    /// <summary>
    ///     Only the migration descriptor: used by hosts that provision tenant databases without serving requests
    ///     (the Migrator).
    /// </summary>
    public static IServiceCollection AddTicketingMigrations(this IServiceCollection services)
    {
        services.AddSingleton(
            new ModuleDatabaseDescriptor(
                ModuleName,
                DatabaseScope.Tenant,
                connectionString => new TicketingDbContext(
                    new DbContextOptionsBuilder<TicketingDbContext>()
                        .UseNpgsql(connectionString, Configure)
                        .Options)));

        return services;
    }

    internal static void Configure(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MigrationsHistoryTable(TicketingDbContext.MigrationsHistoryTable, TicketingDbContext.Schema);
}
