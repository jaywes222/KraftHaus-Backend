using Common.Domain.StronglyTypedIds;
using Identity.Domain.Common;
using Identity.Domain.Invitations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Identity.Infrastructure.Persistence;

public sealed class StaffInvitationConfiguration
    : IEntityTypeConfiguration<StaffInvitation>
{
    public void Configure(
        EntityTypeBuilder<StaffInvitation> builder)
    {
        builder.ToTable("staff_invitation");

        builder.HasKey(x => x.Id);

        builder.Ignore(x => x.Events);

        builder.Property(x => x.Id)
            .HasConversion(
                IdentityEfConversions
                    .IdConverter<StaffInvitationId>());

        builder.Property(x => x.InvitedByUserId)
            .HasConversion(
                IdentityEfConversions
                    .IdConverter<ApplicationUserId>())
            .IsRequired();

        builder.Property(x => x.Channel)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.Destination)
            .HasMaxLength(320)
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

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        builder.Property(x => x.AcceptedAt);

        builder.Property(x => x.RevokedAt);

        builder.HasIndex(
                x => new
                {
                    x.Destination,
                    x.Status
                });

        builder.Property(x => x.Version)
            .HasColumnName("aggregate_version")
            .IsConcurrencyToken();

        IdentityEfConversions.ConfigureAudit(builder);
    }
}
