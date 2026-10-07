using Tenancy.Domain.Common;

namespace Tenancy.Application.Abstractions;

public interface ITenantDatabaseConnectionFactory
{
    string Create(TenantId tenantId, string databaseName);
}
