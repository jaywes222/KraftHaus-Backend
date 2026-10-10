using Microsoft.Extensions.Caching.Memory;
using Common.Domain.ResultMonad;
using Tenancy.Application.Abstractions;
using Tenancy.Domain.Tenants;
using Common.Domain.Tenancy;

namespace Tenancy.Infrastructure.Resolution;

public sealed class TenantResolutionService(
    ITenantRegistry registry,
    IMemoryCache cache) : ITenantResolutionService
{
    public async Task<Result<TenantDescriptor>> ResolveAsync(string host, Guid? tid, CancellationToken cancellationToken)
    {
        var hostResult = HostName.Create(host);
        if (hostResult.IsFailure)
        {
            return hostResult.Error!;
        }

        var version = await registry.GetVersionAsync(cancellationToken);
        var key = $"tenant-host:{version}:{hostResult.Value!.Value}";
        var descriptor = await cache.GetOrCreateAsync(key, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1);
            return await registry.FindByHostAsync(hostResult.Value, cancellationToken);
        });

        if (descriptor is null)
        {
            return Error.NotFound("host", hostResult.Value.Value);
        }

        if (descriptor.Status != TenantStatus.Active)
        {
            return new Error { Key = "Tenant.Inactive", Description = "The resolved tenant is not active." };
        }

        if (tid.HasValue && descriptor.TenantId.Value != tid.Value)
        {
            return new Error
            {
                Key = "Tenant.TidMismatch",
                Description = "The tid claim does not match the tenant resolved from the host.",
                StatusCode = System.Net.HttpStatusCode.Forbidden
            };
        }

        return descriptor;
    }
}
