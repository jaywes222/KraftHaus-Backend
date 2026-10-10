using Common.Domain.Tenancy;
using Microsoft.Extensions.Options;
using Npgsql;
using Tenancy.Application.Abstractions;
using Tenancy.Domain.Common;
using Tenancy.Infrastructure.Options;

namespace Tenancy.Infrastructure.Resolution;

public sealed class TenantDatabaseConnectionFactory(IOptions<TenancyDatabaseOptions> options) : ITenantDatabaseConnectionFactory
{
    private readonly TenancyDatabaseOptions _options = options.Value;

    public string Create(TenantId tenantId, string databaseName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);
        var builder = new NpgsqlConnectionStringBuilder(_options.PlatformConnectionString)
        {
            Database = databaseName
        };
        return builder.ConnectionString;
    }
}
