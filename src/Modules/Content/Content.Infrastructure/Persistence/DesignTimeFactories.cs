using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Content.Infrastructure.Persistence;

/// <summary>
///     Lets <c>dotnet ef</c> build the model without a resolved tenant. Point it at any tenant database (for example
///     the development tenant <c>krafthaus_dev</c>) with <c>--connection=...</c> or <c>KRAFTHAUS_TENANT_CONNECTION</c>.
/// </summary>
public sealed class ContentDbContextFactory : IDesignTimeDbContextFactory<ContentDbContext>
{
    private const string ConnectionOptionPrefix = "--connection=";
    private const string EnvironmentVariable = "KRAFTHAUS_TENANT_CONNECTION";
    private const string Fallback = "Host=localhost;Database=krafthaus_dev;Username=postgres";

    public ContentDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            args.FirstOrDefault(x => x.StartsWith(ConnectionOptionPrefix, StringComparison.OrdinalIgnoreCase))
                ?[ConnectionOptionPrefix.Length..]
            ?? Environment.GetEnvironmentVariable(EnvironmentVariable)
            ?? Fallback;

        return new ContentDbContext(
            new DbContextOptionsBuilder<ContentDbContext>()
                .UseNpgsql(
                    connectionString,
                    npgsql => npgsql.MigrationsHistoryTable(
                        ContentDbContext.MigrationsHistoryTable,
                        ContentDbContext.Schema))
                .Options);
    }
}
