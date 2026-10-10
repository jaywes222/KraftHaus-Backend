using Common.Application.Persistence;
using Common.Domain.Tenancy;
using Tenancy.Application.Abstractions;
using Tenancy.Infrastructure.Database;
using Xunit;

namespace Tenancy.Tests;

public sealed class InfrastructureTests
{
    [Fact]
    public void IdentityGuardAcceptsExactMatch()
    {
        var tenantId = Guid.CreateVersion7();

        DatabaseIdentityGuard.Validate("Tenant", tenantId, "krafthaus_t_client_a", DatabaseScope.Tenant, tenantId, "krafthaus_t_client_a");
    }

    [Fact]
    public void IdentityGuardRejectsTenantMismatch()
    {
        var tenantId = Guid.CreateVersion7();

        Assert.Throws<InvalidOperationException>(() =>
            DatabaseIdentityGuard.Validate("Tenant", Guid.CreateVersion7(), "krafthaus_t_client_a", DatabaseScope.Tenant, tenantId, "krafthaus_t_client_a"));
    }

    [Fact]
    public void IdentityGuardRejectsPlatformImpersonation()
    {
        var tenantId = Guid.CreateVersion7();

        Assert.Throws<InvalidOperationException>(() =>
            DatabaseIdentityGuard.Validate("Tenant", tenantId, "krafthaus_t_client_a", DatabaseScope.Platform, null, "krafthaus_platform"));
    }

[Fact]
public void InitialTenancyMigrationIsCheckedIn()
    {
        var root = FindRepositoryRoot();

        var migration = Path.Combine(
            root,
            "src",
            "Modules",
            "Tenancy.Infrastructure",
            "Persistence",
            "Migrations",
            "20261007023635_InitialTenancy.cs");

        Assert.True(File.Exists(migration));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null &&
               !File.Exists(
                   Path.Combine(
                       directory.FullName,
                       "Directory.Packages.props")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException(
                "Repository root not found.");
    }
}

public sealed class ProvisioningTests
{
    [Fact]
    public async Task InitializeAllResumesByInitializingEachNonDisabledTenant()
    {
        var active = new TenantDescriptor(
            new TenantId(Guid.CreateVersion7()),
            Tenancy.Domain.Tenants.TenantSlug.Create("active").Value!,
            IdentityScope.Tenant,
            Tenancy.Domain.Tenants.TenantStatus.Active,
            "krafthaus_t_active");
        var disabled = new TenantDescriptor(
            new TenantId(Guid.CreateVersion7()),
            Tenancy.Domain.Tenants.TenantSlug.Create("disabled").Value!,
            IdentityScope.Tenant,
            Tenancy.Domain.Tenants.TenantStatus.Disabled,
            "krafthaus_t_disabled");
        var registry = new ProvisioningRegistry([active, disabled]);
        var initializer = new RecordingInitializer();
        var service = new Tenancy.Application.Provisioning.TenantProvisioningService(registry, new TestConnectionFactory(), initializer);

        var result = await service.InitializeAllAsync(CancellationToken.None);

        Assert.False(result.IsFailure);
        Assert.Equal(2, initializer.Calls);
        Assert.Contains(active.TenantId.Value, initializer.TenantIds);
        Assert.Contains(Guid.Empty, initializer.TenantIds); // platform initialization
    }

    private sealed class ProvisioningRegistry(IReadOnlyCollection<TenantDescriptor> tenants) : ITenantRegistry
    {
        public Task<TenantDescriptor?> FindByHostAsync(Tenancy.Domain.Tenants.HostName hostName, CancellationToken cancellationToken) => Task.FromResult<TenantDescriptor?>(null);
        public Task<TenantDescriptor?> FindByIdAsync(TenantId tenantId, CancellationToken cancellationToken) => Task.FromResult(tenants.SingleOrDefault(x => x.TenantId == tenantId));
        public Task<IReadOnlyCollection<TenantDescriptor>> GetAllAsync(CancellationToken cancellationToken) => Task.FromResult(tenants);
        public Task<long> GetVersionAsync(CancellationToken cancellationToken) => Task.FromResult(1L);
    }

    private sealed class TestConnectionFactory : ITenantDatabaseConnectionFactory
    {
        public string Create(TenantId tenantId, string databaseName) => $"Database={databaseName}";
    }

    private sealed class RecordingInitializer : IDatabaseInitializer
    {
        public int Calls { get; private set; }
        public List<Guid> TenantIds { get; } = [];

        public Task InitializePlatformAsync(CancellationToken cancellationToken)
        {
            Calls++;
            TenantIds.Add(Guid.Empty);
            return Task.CompletedTask;
        }

        public Task InitializeTenantAsync(Guid tenantId, string databaseName, string connectionString, CancellationToken cancellationToken)
        {
            Calls++;
            TenantIds.Add(tenantId);
            return Task.CompletedTask;
        }
    }
}
