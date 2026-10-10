using Common.Application.Persistence;
using Common.Infrastructure.Persistence.Auditing;
using Identity.Infrastructure.Auditing;
using Identity.Infrastructure.IdentityServer;
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
            .Validate(
                x => !string.IsNullOrWhiteSpace(x.PlatformConnectionString),
                "IdentityDatabase:PlatformConnectionString is required (use dotnet user-secrets in development).")
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

            options.AddInterceptors(
                // Stamps CreatedOn/CreatedBy/LastModifiedOn/LastModifiedBy on every auditable entity.
                sp.GetRequiredService<ApplyAuditingInterceptor>(),

                // Projects domain events into identity.security_audit_log in the same transaction (WP-08.2).
                sp.GetRequiredService<IdentityAuditSaveChangesInterceptor>(),

                // Evicts cached session status after a revocation commits (access-token validation, §6.1).
                sp.GetRequiredService<SessionStatusCacheInvalidator>());
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
