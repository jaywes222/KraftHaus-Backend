using Common.Domain.Tenancy;
using Tenancy.Domain.Common;
using Tenancy.Domain.Tenants;

namespace Tenancy.Application.Abstractions;

public interface ITenantRegistry
{
    Task<TenantDescriptor?> FindByHostAsync(
        HostName hostName,
        CancellationToken cancellationToken);

    Task<TenantDescriptor?> FindByIdAsync(
        TenantId tenantId,
        CancellationToken cancellationToken);

    Task<IReadOnlyCollection<TenantDescriptor>> GetAllAsync(
        CancellationToken cancellationToken);

    Task<long> GetVersionAsync(
        CancellationToken cancellationToken);
}

public sealed record TenantDescriptor(
    TenantId TenantId,
    TenantSlug Slug,
    IdentityScope IdentityScope,
    TenantStatus Status,
    string DatabaseName);

public interface ITenantContext
{
    TenantId TenantId { get; }

    IdentityScope IdentityScope { get; }

    bool IsResolved { get; }
}

public interface ITenantScopeFactory
{
    ValueTask<IDisposable> CreateAsync(
        TenantDescriptor descriptor,
        CancellationToken cancellationToken);
}
