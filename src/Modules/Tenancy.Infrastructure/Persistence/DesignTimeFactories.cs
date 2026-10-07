using System.Security.Policy;
using Microsoft.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Polly;
using static System.Net.Mime.MediaTypeNames;

namespace Tenancy.Infrastructure.Persistence;

public sealed class TenancyDesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<TenancyDbContext>
{
    public TenancyDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            DesignTimeConnectionString.Resolve(
                args,
                "KRAFTHAUS_PLATFORM_CONNECTION");

        var options =
            new DbContextOptionsBuilder<TenancyDbContext>()
                .UseNpgsql(
                    connectionString,
                    x => x.MigrationsHistoryTable(
                        "__ef_migrations_history",
                        "tenancy"))
                .Options;

        return new TenancyDbContext(options);
    }
}

public sealed class InfraDesignTimeDbContextFactory
    : IDesignTimeDbContextFactory<InfraDbContext>
{
    public InfraDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            DesignTimeConnectionString.Resolve(
                args,
                "KRAFTHAUS_PLATFORM_CONNECTION");

        var options =
            new DbContextOptionsBuilder<InfraDbContext>()
                .UseNpgsql(
                    connectionString,
                    x => x.MigrationsHistoryTable(
                        "__ef_migrations_history",
                        "infra"))
                .Options;

        return new InfraDbContext(options);
    }
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
