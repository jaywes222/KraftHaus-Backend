using Common.Domain.Tenancy;

namespace Common.Application.Tenancy;

/// <summary>A tenant as seen from one of its host names.</summary>
public sealed record TenantHostInfo(
    TenantId TenantId,
    IdentityScope IdentityScope);

/// <summary>
///     Looks a tenant up by host name outside a request (seeders, jobs). Implemented by the Tenancy module so other
///     modules can depend on it without referencing Tenancy; inside a request use <see cref="ICurrentTenant" />.
/// </summary>
public interface ITenantDirectory
{
    Task<TenantHostInfo?> FindByHostAsync(
        string hostName,
        CancellationToken cancellationToken);
}
