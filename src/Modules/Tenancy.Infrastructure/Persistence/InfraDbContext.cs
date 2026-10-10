using Common.Domain.Tenancy;
using Common.Infrastructure.Persistence.ValueConverters;
using Microsoft.EntityFrameworkCore;
using Tenancy.Domain.Common;

namespace Tenancy.Infrastructure.Persistence;

public sealed class InfraDbContext(DbContextOptions<InfraDbContext> options) : DbContext(options)
{
    public DbSet<DatabaseIdentity> DatabaseIdentities => Set<DatabaseIdentity>();
    public DbSet<ProvisioningAttempt> ProvisioningAttempts => Set<ProvisioningAttempt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("infra");
        modelBuilder.ApplyConfiguration(new InfraConfiguration());
        modelBuilder.ApplyConfiguration(new ProvisioningAttemptConfiguration());
        base.OnModelCreating(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<UtcDateTimeOffsetConverter>();
        base.ConfigureConventions(configurationBuilder);
    }
}

public enum DatabaseIdentityKind
{
    Platform = 1,
    Tenant = 2
}

public sealed class DatabaseIdentity(DatabaseIdentityId id) : Common.Domain.Entities.AuditableEntity<DatabaseIdentityId>(id)
{
    private DatabaseIdentity() : this(new DatabaseIdentityId(Guid.Empty)) { }

    public required DatabaseIdentityKind Kind { get; init; }
    public Guid? TenantId { get; init; }
    public required string DatabaseName { get; init; }
    public string SchemaVersion { get; private set; } = default!;

    public static DatabaseIdentity Create(DatabaseIdentityId id, DatabaseIdentityKind kind, Guid? tenantId, string databaseName)
        => new(id)
        {
            Kind = kind,
            TenantId = tenantId,
            DatabaseName = databaseName,
            SchemaVersion = "1"
        };
}

public enum ProvisioningAttemptStatus
{
    Started = 1,
    Succeeded = 2,
    Failed = 3
}

public sealed class ProvisioningAttempt(Guid id)
{
    private ProvisioningAttempt() : this(Guid.Empty) { }

    public Guid Id { get; private init; } = id;
    public required Guid TenantId { get; init; }
    public required string DatabaseName { get; init; }
    public required int Attempt { get; init; }
    public required ProvisioningAttemptStatus Status { get; set; }
    public string? Error { get; set; }
    public DateTimeOffset StartedOn { get; init; }
    public DateTimeOffset? CompletedOn { get; set; }
}
