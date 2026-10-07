using Common.Application.Persistence;
using Identity.Infrastructure.Options;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Identity.Infrastructure.Database;

public static class IdentityDatabaseRegistration
{
    public static IServiceCollection AddIdentityPersistence(
        this IServiceCollection services)
    {
        services
            .AddOptions<IdentityDatabaseOptions>()
            .BindConfiguration("IdentityDatabase")
            .ValidateOnStart();

        services.AddDbContext<IdentityDbContext>((sp, options) =>
        {
            var databaseOptions =
                sp.GetRequiredService<IOptions<IdentityDatabaseOptions>>()
                    .Value;

            options.UseNpgsql(
                databaseOptions.PlatformConnectionString,
                npgsql =>
                {
                    npgsql.MigrationsHistoryTable(
                        "__ef_migrations_history",
                        "identity");
                });
        });

        services.AddSingleton(
            new ModuleDatabaseDescriptor(
                "Identity",
                DatabaseScope.Platform,
                connectionString =>
                {
                    var options =
                        new DbContextOptionsBuilder<IdentityDbContext>()
                            .UseNpgsql(
                                connectionString,
                                npgsql =>
                                {
                                    npgsql.MigrationsHistoryTable(
                                        "__ef_migrations_history",
                                        "identity");
                                })
                            .Options;

                    return new IdentityDbContext(options);
                }));

        return services;
    }
}
