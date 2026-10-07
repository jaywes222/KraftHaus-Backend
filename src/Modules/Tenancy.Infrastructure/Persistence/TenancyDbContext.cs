using Common.Infrastructure.Persistence.ValueConverters;
using Microsoft.EntityFrameworkCore;
using Tenancy.Domain.Tenants;

namespace Tenancy.Infrastructure.Persistence;

public sealed class TenancyDbContext(DbContextOptions<TenancyDbContext> options) : DbContext(options)
{
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantHost> TenantHosts => Set<TenantHost>();
    public DbSet<PolicyVersion> PolicyVersions => Set<PolicyVersion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("tenancy");
        modelBuilder.ApplyConfiguration(new TenantConfiguration());
        modelBuilder.ApplyConfiguration(new TenantHostConfiguration());
        modelBuilder.ApplyConfiguration(new PolicyVersionConfiguration());
        base.OnModelCreating(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>();
        base.ConfigureConventions(configurationBuilder);
    }
}
