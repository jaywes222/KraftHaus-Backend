using System.Collections.Concurrent;
using Common.Domain.Tenancy;
using Npgsql;
using Tenancy.Application.Abstractions;
using Tenancy.Domain.Common;

namespace Tenancy.Infrastructure.Resolution;

public sealed class TenantDataSourceProvider(
    ITenantRegistry registry,
    ITenantDatabaseConnectionFactory connectionFactory) : IAsyncDisposable
{
    private readonly ConcurrentDictionary<Guid, NpgsqlDataSource> _dataSources = new();

    public async Task<NpgsqlDataSource> GetDataSourceAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        var descriptor = await registry.FindByIdAsync(tenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant '{tenantId.Value}' was not found.");
        return _dataSources.GetOrAdd(tenantId.Value, _ => NpgsqlDataSource.Create(connectionFactory.Create(tenantId, descriptor.DatabaseName)));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var dataSource in _dataSources.Values)
        {
            await dataSource.DisposeAsync();
        }
    }
}
