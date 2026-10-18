using Common.Application.Persistence;
using Common.Application.Tenancy;
using Common.Infrastructure.Persistence.Auditing;
using EntityFramework.Exceptions.PostgreSQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;
using Music.Infrastructure.Auditing;
using Music.Infrastructure.Persistence;

namespace Music.Infrastructure.Database;

public static class MusicDatabaseRegistration
{
    public const string ModuleName = "Music";

    /// <summary>
    ///     Request-time <see cref="MusicDbContext" /> bound to the resolved tenant's database, plus the migration
    ///     descriptor (tenant scope) so provisioning applies the Music schema to every tenant database.
    /// </summary>
    public static IServiceCollection AddMusicPersistence(this IServiceCollection services)
    {
        services.AddScoped<MusicAuditSaveChangesInterceptor>();

        services.AddDbContext<MusicDbContext>((sp, options) =>
        {
            var tenantDatabase = sp.GetRequiredService<ITenantDatabaseConnection>();

            options
                .UseNpgsql(tenantDatabase.GetConnectionString(), Configure)
                .UseExceptionProcessor()
                .AddInterceptors(
                    // CreatedOn/CreatedBy/LastModifiedOn/LastModifiedBy on every auditable row.
                    sp.GetRequiredService<ApplyAuditingInterceptor>(),

                    // music.audit_log rows in the same transaction as the change.
                    sp.GetRequiredService<MusicAuditSaveChangesInterceptor>());
        });

        return services.AddMusicMigrations();
    }

    /// <summary>
    ///     Only the migration descriptor: used by hosts that provision tenant databases without serving requests
    ///     (the Migrator).
    /// </summary>
    public static IServiceCollection AddMusicMigrations(this IServiceCollection services)
    {
        services.AddSingleton(
            new ModuleDatabaseDescriptor(
                ModuleName,
                DatabaseScope.Tenant,
                connectionString => new MusicDbContext(
                    new DbContextOptionsBuilder<MusicDbContext>()
                        .UseNpgsql(connectionString, Configure)
                        .Options)));

        return services;
    }

    internal static void Configure(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MigrationsHistoryTable(MusicDbContext.MigrationsHistoryTable, MusicDbContext.Schema);
}
