using Common.Domain.StronglyTypedIds;
using Identity.Domain.Common;
using Identity.Domain.Roles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Identity.Infrastructure.Persistence;

public sealed class RoleAssignmentConfiguration
    : IEntityTypeConfiguration<RoleAssignment>
{
    public void Configure(
        EntityTypeBuilder<RoleAssignment> builder)
    {
        builder.ToTable("role_assignment");

        builder.HasKey(x => x.Id);

        builder.Ignore(x => x.Events);

        builder.Property(x => x.Id)
            .HasConversion(
                IdentityEfConversions
                    .IdConverter<RoleAssignmentId>());

        builder.Property(x => x.UserId)
            .HasConversion(
                IdentityEfConversions
                    .IdConverter<ApplicationUserId>())
            .IsRequired();

        builder.Property(x => x.Role)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.ScopeType)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.ScopeId);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.GrantedAt)
            .IsRequired();

        builder.Property(x => x.RevokedAt);

        /*
         * PostgreSQL:
         * NULLS NOT DISTINCT means two platform-style rows
         * containing NULL ScopeId cannot duplicate each other.
         *
         * Only active assignments participate in the unique
         * constraint.
         */
        builder.HasIndex(
                x => new
                {
                    x.UserId,
                    x.Role,
                    x.ScopeType,
                    x.ScopeId
                })
            .IsUnique()
            .HasFilter(
                "\"Status\" = 'Active'")
            .AreNullsDistinct(false);

        builder.Property(x => x.Version)
            .HasColumnName("aggregate_version")
            .IsConcurrencyToken();

        IdentityEfConversions.ConfigureAudit(builder);
    }
}
