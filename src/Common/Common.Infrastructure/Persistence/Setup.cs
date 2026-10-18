using Common.Application.Options;
using Common.Application.Persistence;
using Common.Infrastructure.Communications.Email;
using Common.Infrastructure.Notifications;
using Common.Infrastructure.Notifications.Sms;
using Common.Infrastructure.Persistence.Auditing;
using Common.Infrastructure.Persistence.AuditLog;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Common.Infrastructure.Persistence;

public static class Setup
{
    public static IServiceCollection AddCommonPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        return services
            // Single shared NpgsqlDataSource → one bounded connection pool for the whole app.
            // Every DbContext, the audit-log retention worker, and the PostgreSQL health check
            // resolve this same instance, so they share one pool instead of each creating
            // its own data source and connection pool.
            .AddSingleton(sp =>
            {
                var connectionString =
                    sp.GetRequiredService<IOptions<DatabaseOptions>>()
                        .Value
                        .ConnectionString;

                return new NpgsqlDataSourceBuilder(connectionString).Build();
            })

            // Application-wide module database registry.
            // Individual modules register their database descriptors against this
            // shared registry; the registry itself must only have one singleton instance.
            .AddSingleton<IModuleDatabaseRegistry, ModuleDatabaseRegistry>()

            // Explicit TimeProvider registration.
            // Infrastructure services and EF interceptors depend on TimeProvider.
            .AddSingleton(TimeProvider.System)

            .AddSmsGateway(configuration)
            .AddEmailGateway(configuration)

            // Every transactional SMS / e-mail gets a row in Communications (when that module runs here).
            .AddMessageRecording()

            .AddAuditLogServices()
            .AddAuditingInterceptors()
            .AddSingleton<SeedingCompletionTracker>()
            .AddHostedService<DatabaseSeederOrchestrator>();
    }
}
