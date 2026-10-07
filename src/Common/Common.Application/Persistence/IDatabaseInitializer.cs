namespace Common.Application.Persistence;

public interface IDatabaseInitializer
{
    Task InitializePlatformAsync(
        CancellationToken cancellationToken);

    Task InitializeTenantAsync(
        Guid tenantId,
        string databaseName,
        string connectionString,
        CancellationToken cancellationToken);
}
