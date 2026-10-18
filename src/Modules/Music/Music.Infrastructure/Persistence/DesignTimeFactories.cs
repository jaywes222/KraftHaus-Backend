using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Music.Infrastructure.Persistence;

/// <summary>
///     Lets <c>dotnet ef</c> build the model without a resolved tenant. Point it at any tenant database (for example
///     the development tenant <c>krafthaus_dev</c>) with <c>--connection=...</c> or <c>KRAFTHAUS_TENANT_CONNECTION</c>.
/// </summary>
public sealed class MusicDbContextFactory : IDesignTimeDbContextFactory<MusicDbContext>
{
    private const string ConnectionOptionPrefix = "--connection=";
    private const string EnvironmentVariable = "KRAFTHAUS_TENANT_CONNECTION";
    private const string Fallback = "Host=localhost;Database=krafthaus_dev;Username=postgres";

    public MusicDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            args.FirstOrDefault(x => x.StartsWith(ConnectionOptionPrefix, StringComparison.OrdinalIgnoreCase))
                ?[ConnectionOptionPrefix.Length..]
            ?? Environment.GetEnvironmentVariable(EnvironmentVariable)
            ?? Fallback;

        return new MusicDbContext(
            new DbContextOptionsBuilder<MusicDbContext>()
                .UseNpgsql(
                    connectionString,
                    npgsql => npgsql.MigrationsHistoryTable(
                        MusicDbContext.MigrationsHistoryTable,
                        MusicDbContext.Schema))
                .Options);
    }
}
