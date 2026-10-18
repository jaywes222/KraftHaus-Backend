using Common.Application.Persistence;
using Common.Application.Tenancy;
using EntityFramework.Exceptions.PostgreSQL;
using Feedback.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Feedback.Infrastructure.Database;

public static class FeedbackDatabaseRegistration
{
    public const string ModuleName = "Feedback";

    /// <summary>
    ///     Request-time <see cref="FeedbackDbContext" /> on the resolved tenant's database. Deliberately without the
    ///     auditing interceptors: nothing may stamp who wrote a rating.
    /// </summary>
    public static IServiceCollection AddFeedbackPersistence(this IServiceCollection services)
    {
        services.AddDbContext<FeedbackDbContext>((sp, options) =>
            options
                .UseNpgsql(sp.GetRequiredService<ITenantDatabaseConnection>().GetConnectionString(), Configure)
                .UseExceptionProcessor());

        return services.AddFeedbackMigrations();
    }

    /// <summary>Only the migration descriptor (the Migrator).</summary>
    public static IServiceCollection AddFeedbackMigrations(this IServiceCollection services)
    {
        services.AddSingleton(
            new ModuleDatabaseDescriptor(
                ModuleName,
                DatabaseScope.Tenant,
                connectionString => new FeedbackDbContext(
                    new DbContextOptionsBuilder<FeedbackDbContext>()
                        .UseNpgsql(connectionString, Configure)
                        .Options)));

        return services;
    }

    internal static void Configure(NpgsqlDbContextOptionsBuilder npgsql) =>
        npgsql.MigrationsHistoryTable(FeedbackDbContext.MigrationsHistoryTable, FeedbackDbContext.Schema);
}
