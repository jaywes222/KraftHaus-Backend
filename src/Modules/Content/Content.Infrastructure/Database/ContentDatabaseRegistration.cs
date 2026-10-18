using Common.Application.Persistence;
using Common.Application.Tenancy;
using Common.Infrastructure.Persistence.Auditing;
using EntityFramework.Exceptions.PostgreSQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using Content.Infrastructure.Auditing;
using Content.Infrastructure.Persistence;

namespace Content.Infrastructure.Database;

public static class ContentDatabaseRegistration
{
    public const string ModuleName = "Content";

    /// <summary>
    ///     Request-time <see cref="ContentDbContext" /> bound to the resolved tenant's database, plus the migration
    ///     descriptor (tenant scope) so provisioning applies the Content schema to every tenant database.
    /// </summary>
    public static IServiceCollection AddContentPersistence(this IServiceCollection services)
    {
        services.AddScoped<ContentAuditSaveChangesInterceptor>();

        services.AddDbContext<ContentDbContext>((sp, options) =>
        {
            var tenantDatabase = sp.GetRequiredService<ITenantDatabaseConnection>();

            options
                .UseNpgsql(tenantDatabase.GetConnectionString(), Configure)
                .UseExceptionProcessor()
                .AddInterceptors(
                    // CreatedOn/CreatedBy/LastModifiedOn/LastModifiedBy on every auditable row.
                    sp.GetRequiredService<ApplyAuditingInterceptor>(),

                    // content.audit_log rows in the same transaction as the change.
                    sp.GetRequiredService<ContentAuditSaveChangesInterceptor>());
        });

        return services.AddContentMigrations();
    }

    /// <summary>
    ///     Only the migration descriptor: used by hosts that provision tenant databases without serving requests
    ///     (the Migrator).
    /// </summary>
    public static IServiceCollection AddContentMigrations(this IServiceCollection services)
    {
        services.AddSingleton(
            new ModuleDatabaseDescriptor(
                ModuleName,
                DatabaseScope.Tenant,
                connectionString => new ContentDbContext(
                    new DbContextOptionsBuilder<ContentDbContext>()
                        .UseNpgsql(connectionString, Configure)
                        .Options)));

        return services;
    }

    internal static void Configure(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MigrationsHistoryTable(ContentDbContext.MigrationsHistoryTable, ContentDbContext.Schema);
}
