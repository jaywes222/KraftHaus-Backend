using Common.Application.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tenancy.Infrastructure.Options;
using Tenancy.Infrastructure.Persistence;

namespace Tenancy.Infrastructure.Database;

public static class TenancyDatabaseRegistration
{
    public static IServiceCollection AddTenancyPersistence(
        this IServiceCollection services)
    {
        services
            .AddOptions<TenancyDatabaseOptions>()
            .BindConfiguration("TenancyDatabase")
            .ValidateOnStart();

        // Required by TenantRegistry and other singleton tenancy services.
        services.AddDbContextFactory<TenancyDbContext>((sp, options) =>
        {
            var databaseOptions =
                sp.GetRequiredService<IOptions<TenancyDatabaseOptions>>()
                    .Value;

            options.UseNpgsql(
                databaseOptions.PlatformConnectionString,
                npgsql =>
                {
                    npgsql.MigrationsHistoryTable(
                        "__ef_migrations_history",
                        "tenancy");
                });
        });

        services.AddDbContext<InfraDbContext>((sp, options) =>
        {
            var databaseOptions =
                sp.GetRequiredService<IOptions<TenancyDatabaseOptions>>()
                    .Value;

            options.UseNpgsql(
                databaseOptions.PlatformConnectionString,
                npgsql =>
                {
                    npgsql.MigrationsHistoryTable(
                        "__ef_migrations_history",
                        "infra");
                });
        });

        services.AddSingleton(
            new ModuleDatabaseDescriptor(
                "Tenancy",
                DatabaseScope.Platform,
                connectionString =>
                {
                    var options =
                        new DbContextOptionsBuilder<TenancyDbContext>()
                            .UseNpgsql(
                                connectionString,
                                npgsql =>
                                {
                                    npgsql.MigrationsHistoryTable(
                                        "__ef_migrations_history",
                                        "tenancy");
                                })
                            .Options;

                    return new TenancyDbContext(options);
                }));

        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();

        return services;
    }
}
