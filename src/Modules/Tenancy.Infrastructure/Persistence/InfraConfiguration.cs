using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Tenancy.Domain.Common;

namespace Tenancy.Infrastructure.Persistence;

public sealed class InfraConfiguration : IEntityTypeConfiguration<DatabaseIdentity>
{
    public void Configure(EntityTypeBuilder<DatabaseIdentity> builder)
    {
        builder.ToTable("database_identity");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(
                new ValueConverter<DatabaseIdentityId, Guid>(
                    x => x.Value,
                    x => new DatabaseIdentityId(x)));

        builder.Property(x => x.Kind)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.TenantId);

        builder.Property(x => x.DatabaseName)
            .HasMaxLength(63)
            .IsRequired();

        builder.Property(x => x.SchemaVersion)
            .HasMaxLength(32)
            .IsRequired();

        builder.HasIndex(x => new { x.Kind, x.TenantId })
            .IsUnique();

        TenancyEfConversions.ConfigureAudit(builder);
    }
}

public sealed class ProvisioningAttemptConfiguration
    : IEntityTypeConfiguration<ProvisioningAttempt>
{
    public void Configure(EntityTypeBuilder<ProvisioningAttempt> builder)
    {
        builder.ToTable("provisioning_attempt");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.DatabaseName)
            .HasMaxLength(63)
            .IsRequired();

        builder.Property(x => x.Error)
            .HasMaxLength(4000);

        builder.Property(x => x.StartedOn)
            .IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.Attempt })
            .IsUnique();
    }
}
