using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Payments.Infrastructure.Persistence;

/// <summary>
///     Lets <c>dotnet ef</c> build the model without a resolved tenant. Point it at any tenant database (for example
///     the development tenant <c>krafthaus_dev</c>) with <c>--connection=...</c> or <c>KRAFTHAUS_TENANT_CONNECTION</c>.
/// </summary>
public sealed class PaymentsDbContextFactory : IDesignTimeDbContextFactory<PaymentsDbContext>
{
    private const string ConnectionOptionPrefix = "--connection=";
    private const string EnvironmentVariable = "KRAFTHAUS_TENANT_CONNECTION";
    private const string Fallback = "Host=localhost;Database=krafthaus_dev;Username=postgres";

    public PaymentsDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            args.FirstOrDefault(x => x.StartsWith(ConnectionOptionPrefix, StringComparison.OrdinalIgnoreCase))
                ?[ConnectionOptionPrefix.Length..]
            ?? Environment.GetEnvironmentVariable(EnvironmentVariable)
            ?? Fallback;

        return new PaymentsDbContext(
            new DbContextOptionsBuilder<PaymentsDbContext>()
                .UseNpgsql(
                    connectionString,
                    npgsql => npgsql.MigrationsHistoryTable(
                        PaymentsDbContext.MigrationsHistoryTable,
                        PaymentsDbContext.Schema))
                .Options);
    }
}
