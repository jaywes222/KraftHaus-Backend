using Common.Application.Persistence;
using Common.Application.Tenancy;
using Common.Infrastructure.Persistence.Auditing;
using EntityFramework.Exceptions.PostgreSQL;
using Events.Infrastructure.Auditing;
using Events.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Events.Infrastructure.Database;

public static class EventsDatabaseRegistration
{
    public const string ModuleName = "Events";

    /// <summary>
    ///     Request-time <see cref="EventsDbContext" /> bound to the resolved tenant's database, plus the migration
    ///     descriptor (tenant scope) so provisioning applies the Events schema to every tenant database.
    /// </summary>
    public static IServiceCollection AddEventsPersistence(this IServiceCollection services)
    {
        services.AddScoped<EventsAuditSaveChangesInterceptor>();

        services.AddDbContext<EventsDbContext>((sp, options) =>
        {
            var tenantDatabase = sp.GetRequiredService<ITenantDatabaseConnection>();

            options
                .UseNpgsql(tenantDatabase.GetConnectionString(), Configure)
                .UseExceptionProcessor()
                .AddInterceptors(
                    // CreatedOn/CreatedBy/LastModifiedOn/LastModifiedBy on every auditable row.
                    sp.GetRequiredService<ApplyAuditingInterceptor>(),

                    // events.audit_log rows in the same transaction as the change.
                    sp.GetRequiredService<EventsAuditSaveChangesInterceptor>());
        });

        return services.AddEventsMigrations();
    }

    /// <summary>
    ///     Only the migration descriptor: used by hosts that provision tenant databases without serving requests
    ///     (the Migrator).
    /// </summary>
    public static IServiceCollection AddEventsMigrations(this IServiceCollection services)
    {
        services.AddSingleton(
            new ModuleDatabaseDescriptor(
                ModuleName,
                DatabaseScope.Tenant,
                connectionString => new EventsDbContext(
                    new DbContextOptionsBuilder<EventsDbContext>()
                        .UseNpgsql(connectionString, Configure)
                        .Options)));

        return services;
    }

    internal static void Configure(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MigrationsHistoryTable(EventsDbContext.MigrationsHistoryTable, EventsDbContext.Schema);
}
