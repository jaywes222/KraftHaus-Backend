using Common.Domain.Entities;
using Common.Domain.StronglyTypedIds;
using Common.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Tenancy.Domain.Common;
using Tenancy.Domain.Tenants;

namespace Tenancy.Infrastructure.Persistence;

internal static class TenancyEfConversions
{
    internal static void ConfigureAudit<TEntity>(
        EntityTypeBuilder<TEntity> builder)
        where TEntity : AuditableEntity
    {
        builder.Property(x => x.CreatedBy)
            .HasConversion(
                id => id.HasValue
                    ? id.Value.Value
                    : (Guid?)null,
                value => value.HasValue
                    ? new ApplicationUserId(value.Value)
                    : null);

        builder.Property(x => x.LastModifiedBy)
            .HasConversion(
                id => id.HasValue
                    ? id.Value.Value
                    : (Guid?)null,
                value => value.HasValue
                    ? new ApplicationUserId(value.Value)
                    : null);

        builder.Property(x => x.CreatedOn)
            .IsRequired();

        builder.Property(x => x.LastModifiedOn);
    }
}

public sealed class TenantConfiguration
    : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenant");

        builder.HasKey(x => x.Id);

        builder.Ignore(x => x.Events);

        builder.Property(x => x.Id)
            .HasConversion(
                new ValueConverter<TenantId, Guid>(
                    x => x.Value,
                    x => new TenantId(x)));

        builder.Property(x => x.Slug)
            .HasConversion(
                new ValueConverter<TenantSlug, string>(
                    x => x.Value,
                    x => TenantSlug.FromPersistence(x)))
            .HasMaxLength(63)
            .IsRequired();

        builder.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.IdentityScope)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.DatabaseName)
            .HasMaxLength(63);

        builder.HasIndex(x => x.Slug)
            .IsUnique();

        builder.HasMany(x => x.Hosts)
            .WithOne()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.PolicyVersions)
            .WithOne()
            .HasForeignKey(x => x.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        TenancyEfConversions.ConfigureAudit(builder);

        builder.Property(x => x.Version)
            .HasColumnName("aggregate_version")
            .IsConcurrencyToken();
    }
}

public sealed class TenantHostConfiguration
    : IEntityTypeConfiguration<TenantHost>
{
    public void Configure(EntityTypeBuilder<TenantHost> builder)
    {
        builder.ToTable("tenant_host");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(
                new ValueConverter<TenantHostId, Guid>(
                    x => x.Value,
                    x => new TenantHostId(x)));

        builder.Property(x => x.TenantId)
            .HasConversion(
                new ValueConverter<TenantId, Guid>(
                    x => x.Value,
                    x => new TenantId(x)));

        builder.Property(x => x.HostName)
            .HasConversion(
                new ValueConverter<HostName, string>(
                    x => x.Value,
                    x => HostName.FromPersistence(x)))
            .HasMaxLength(253)
            .IsRequired();

        builder.Property(x => x.Surface)
            .HasConversion<int>()
            .IsRequired();

        builder.HasIndex(x => x.HostName)
            .IsUnique();

        builder.HasIndex(x => x.TenantId)
                    .IsUnique()
                    .HasFilter(
                        "\"IsPrimary\" = TRUE AND \"Enabled\" = TRUE");

        TenancyEfConversions.ConfigureAudit(builder);
    }
}

public sealed class PolicyVersionConfiguration
    : IEntityTypeConfiguration<PolicyVersion>
{
    public void Configure(EntityTypeBuilder<PolicyVersion> builder)
    {
        builder.ToTable("policy_version");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(
                new ValueConverter<PolicyVersionId, Guid>(
                    x => x.Value,
                    x => new PolicyVersionId(x)));

        builder.Property(x => x.TenantId)
            .HasConversion(
                new ValueConverter<TenantId, Guid>(
                    x => x.Value,
                    x => new TenantId(x)))
            .IsRequired();

        builder.Property(x => x.PolicyKey)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.PolicyHash)
            .HasMaxLength(128)
            .IsRequired();

        builder.HasIndex(
                x => new
                {
                    x.TenantId,
                    x.PolicyKey,
                    x.VersionNumber
                })
            .IsUnique();

        TenancyEfConversions.ConfigureAudit(builder);
    }
}
