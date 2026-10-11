using Common.Application.Tenancy;
using Tenancy.Application.Abstractions;

namespace Tenancy.Infrastructure.Resolution;

/// <summary>
///     Adapts the request-scoped <see cref="TenantContext" /> to <see cref="ITenantDatabaseConnection" />, so
///     tenant-scoped modules can open the resolved tenant's database without referencing Tenancy (Spec 08 §5.3).
/// </summary>
internal sealed class TenantDatabaseConnection(
    TenantContext context,
    ITenantDatabaseConnectionFactory connectionFactory) : ITenantDatabaseConnection
{
    public bool IsAvailable =>
        context.IsResolved && !string.IsNullOrWhiteSpace(context.DatabaseName);

    public string GetConnectionString()
    {
        if (!IsAvailable)
        {
            throw new InvalidOperationException(
                "No tenant is resolved for this scope, so there is no tenant database to connect to.");
        }

        return connectionFactory.Create(context.TenantId, context.DatabaseName);
    }
}
