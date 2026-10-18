using Api.Infrastructure.BackgroundJobs;
using Common.Application.Messaging;
using Common.Application.Options;
using Hangfire;
using Hangfire.Dashboard;
using Hangfire.PostgreSql;

namespace Api.Infrastructure;

internal static partial class Setup
{
    /// <summary>
    ///     Hangfire on PostgreSQL (schema <c>hangfire</c> in the platform database, Spec 08 §5.1/§9.4). The server only
    ///     runs where <c>BackgroundJobsOptions:IsServer</c> is true; the Worker service will take that role later.
    /// </summary>
    public static IServiceCollection AddBackgroundJobs(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(nameof(BackgroundJobsOptions)).Get<BackgroundJobsOptions>()
                      ?? throw new InvalidOperationException($"{nameof(BackgroundJobsOptions)} is missing.");

        var connectionString = configuration.GetConnectionString("KrafthausPlatform")
                               ?? throw new InvalidOperationException("ConnectionStrings:KrafthausPlatform is missing.");

        var pooled = new Npgsql.NpgsqlConnectionStringBuilder(connectionString) { MaxPoolSize = options.MaxPoolSize };

        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(
                storage => storage.UseNpgsqlConnection(pooled.ConnectionString),
                new PostgreSqlStorageOptions
                {
                    SchemaName = "hangfire",
                    PrepareSchemaIfNecessary = true,
                    QueuePollInterval = TimeSpan.FromSeconds(options.PollingFrequencyInSeconds)
                }));

        if (options.IsServer)
        {
            services.AddHangfireServer(server => server.WorkerCount = options.WorkerCount);
        }

        services.AddScoped<TenantJobRunner>();
        services.AddHostedService<TenantJobScheduler>();

        return services;
    }

    /// <summary>
    ///     The Hangfire dashboard, in Development only and only for requests from the machine itself. It shows job
    ///     arguments and lets anyone trigger jobs, so it is never exposed on a deployed site.
    /// </summary>
    public static WebApplication UseBackgroundJobsDashboard(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            return app;
        }

        var path = app.Configuration.GetSection(nameof(BackgroundJobsOptions)).Get<BackgroundJobsOptions>()?.DashboardPath ?? "/hangfire";

        app.UseHangfireDashboard(path, new DashboardOptions
        {
            Authorization = [new LocalRequestsOnlyAuthorizationFilter()],
            DisplayStorageConnectionString = false
        });

        return app;
    }
}

/// <summary>Registers (or updates) one Hangfire recurring job per <see cref="TenantJobDescriptor" /> at start-up.</summary>
internal sealed class TenantJobScheduler(
    IRecurringJobManager recurringJobs,
    IEnumerable<TenantJobDescriptor> jobs)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var job in jobs)
        {
            var name = job.Name;
            recurringJobs.AddOrUpdate<TenantJobRunner>(
                name,
                runner => runner.RunAsync(name, CancellationToken.None),
                job.Cron,
                new RecurringJobOptions { TimeZone = TimeZoneInfo.Utc });
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
