using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Common.Infrastructure.Extensions;

public static class MigrationExtensions
{
    public static IApplicationBuilder UseMigration<TContext>(this IApplicationBuilder app)
        where TContext : DbContext
    {
        MigrateDatabaseAsync<TContext>(app.ApplicationServices).GetAwaiter().GetResult();

        return app;
    }

    private static async Task MigrateDatabaseAsync<TContext>(IServiceProvider serviceProvider)
        where TContext : DbContext
    {
        using var scope = serviceProvider.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<TContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<TContext>>();

        var contextName = typeof(TContext).Name;

        var pendingMigrations = (await context.Database.GetPendingMigrationsAsync()).ToList();

        if (pendingMigrations.Count == 0)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                logger.LogInformation("No pending database migrations found for {DbContext}.", contextName);
            }
            return;
        }

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Found {Count} pending migration(s) for {DbContext}: {Migrations}",
                pendingMigrations.Count,
                contextName,
                string.Join(", ", pendingMigrations));

            foreach (var migration in pendingMigrations)
            {
                logger.LogInformation("Applying migration '{Migration}' for {DbContext}...", migration, contextName);
            }
        }

        await context.Database.MigrateAsync();

        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Successfully applied all pending migrations for {DbContext}.", contextName);
        }
    }
}
