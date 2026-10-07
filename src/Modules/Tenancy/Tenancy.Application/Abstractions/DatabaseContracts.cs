using Microsoft.EntityFrameworkCore;

namespace Tenancy.Application.Abstractions;

public enum DatabaseScope
{
    Platform = 1,
    Tenant = 2
}

public enum DatabaseIdentityKind
{
    Platform = 1,
    Tenant = 2
}

public sealed record ModuleDatabaseDescriptor(
    string ModuleName,
    DatabaseScope Scope,
    Func<string, DbContext> CreateDbContext);

public interface IModuleDatabaseRegistry
{
    IReadOnlyCollection<ModuleDatabaseDescriptor> GetAll();
    void Register(ModuleDatabaseDescriptor descriptor);
}

public interface IDatabaseInitializer
{
    Task InitializePlatformAsync(CancellationToken cancellationToken);
    Task InitializeTenantAsync(Guid tenantId, string databaseName, string connectionString, CancellationToken cancellationToken);
}

public interface ITenantProvisioner
{
    Task<Common.Domain.ResultMonad.Result> ProvisionAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<Common.Domain.ResultMonad.Result> InitializeAllAsync(CancellationToken cancellationToken);
}
