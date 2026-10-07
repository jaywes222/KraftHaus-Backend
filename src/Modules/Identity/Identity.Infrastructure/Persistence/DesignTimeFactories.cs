using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Identity.Infrastructure.Persistence;

public sealed class IdentityDbContextFactory
    : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            DesignTimeConnectionString.Resolve(
                args,
                "KRAFTHAUS_PLATFORM_CONNECTION");

        var options =
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseNpgsql(
                    connectionString,
                    x => x.MigrationsHistoryTable(
                        "__ef_migrations_history",
                        "identity"))
                .Options;

        return new IdentityDbContext(options);
    }

    internal static class DesignTimeConnectionString
    {
        private const string ConnectionOptionPrefix = "--connection=";
        private const string ConnectionName = "KrafthausPlatform";

        public static string Resolve(
            string[] args,
            string environmentVariable)
        {
            var argument = args.FirstOrDefault(
                x => x.StartsWith(
                    ConnectionOptionPrefix,
                    StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(argument))
            {
                var connectionString =
                    argument[ConnectionOptionPrefix.Length..];

                if (!string.IsNullOrWhiteSpace(connectionString))
                {
                    return connectionString;
                }
            }

            var environmentConnectionString =
                Environment.GetEnvironmentVariable(
                    environmentVariable);

            if (!string.IsNullOrWhiteSpace(environmentConnectionString))
            {
                return environmentConnectionString;
            }

            var configuration =
                new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile(
                        "appsettings.json",
                        optional: true,
                        reloadOnChange: false)
                    .AddJsonFile(
                        "appsettings.Development.json",
                        optional: true,
                        reloadOnChange: false)
                    .Build();

            var configurationConnectionString =
                configuration.GetConnectionString(
                    ConnectionName);

            if (!string.IsNullOrWhiteSpace(
                configurationConnectionString))
            {
                return configurationConnectionString;
            }

            throw new InvalidOperationException(
                "A database connection string is required. " +
                $"Pass --connection=<connection-string>, set " +
                $"{environmentVariable}, or configure " +
                $"ConnectionStrings:{ConnectionName}.");
        }
    }
}
