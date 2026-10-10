using Common.Domain.Tenancy;
using Microsoft.Extensions.Caching.Memory;
using Tenancy.Application.Abstractions;
using Tenancy.Domain.Common;
using Tenancy.Domain.Tenants;
using Tenancy.Infrastructure.Resolution;
using Xunit;

namespace Tenancy.Tests;

public sealed class ResolutionTests
{
    [Fact]
    public async Task ResolutionRejectsTidMismatch()
    {
        var tenantId = new TenantId(Guid.CreateVersion7());

        var registry = new FakeTenantRegistry(
            new TenantDescriptor(
                tenantId,
                TenantSlug.Create("client-a").Value!,
                IdentityScope.Tenant,
                TenantStatus.Active,
                "krafthaus_t_client_a"));

        using var cache = new MemoryCache(
            new MemoryCacheOptions());

        var service = new TenantResolutionService(
            registry,
            cache);

        var result = await service.ResolveAsync(
            "client-a.example.com",
            Guid.CreateVersion7(),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(
            "Tenant.TidMismatch",
            result.Error!.Key);
    }

    [Fact]
    public async Task ResolutionReturnsActiveTenantByHost()
    {
        var tenantId = new TenantId(Guid.CreateVersion7());

        var registry = new FakeTenantRegistry(
            new TenantDescriptor(
                tenantId,
                TenantSlug.Create("client-a").Value!,
                IdentityScope.Tenant,
                TenantStatus.Active,
                "krafthaus_t_client_a"));

        using var cache = new MemoryCache(
            new MemoryCacheOptions());

        var service = new TenantResolutionService(
            registry,
            cache);

        var result = await service.ResolveAsync(
            "client-a.example.com",
            null,
            CancellationToken.None);

        Assert.False(result.IsFailure);
        Assert.NotNull(result.Value);
        Assert.Equal(
            tenantId,
            result.Value.TenantId);
    }

    private sealed class FakeTenantRegistry(
        TenantDescriptor descriptor) : ITenantRegistry
    {
        private static readonly HostName ExpectedHost =
            HostName.Create("client-a.example.com").Value!;

        public Task<TenantDescriptor?> FindByHostAsync(
            HostName hostName,
            CancellationToken cancellationToken)
        {
            var result =
                hostName.Value == ExpectedHost.Value
                    ? descriptor
                    : null;

            return Task.FromResult(result);
        }

        public Task<TenantDescriptor?> FindByIdAsync(
            TenantId tenantId,
            CancellationToken cancellationToken)
            => Task.FromResult<TenantDescriptor?>(
                tenantId == descriptor.TenantId
                    ? descriptor
                    : null);

        public Task<IReadOnlyCollection<TenantDescriptor>> GetAllAsync(
            CancellationToken cancellationToken)
            => Task.FromResult<
                IReadOnlyCollection<TenantDescriptor>>(
                [descriptor]);

        public Task<long> GetVersionAsync(
            CancellationToken cancellationToken)
            => Task.FromResult(1L);
    }
}
