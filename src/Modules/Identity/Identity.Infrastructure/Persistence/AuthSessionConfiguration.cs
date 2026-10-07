using Common.Domain.StronglyTypedIds;
using Identity.Domain.Common;
using Identity.Domain.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Identity.Infrastructure.Persistence;

public sealed class AuthSessionConfiguration
    : IEntityTypeConfiguration<AuthSession>
{
    public void Configure(
        EntityTypeBuilder<AuthSession> builder)
    {
        builder.ToTable("auth_session");

        builder.HasKey(x => x.Id);

        builder.Ignore(x => x.Events);

        builder.Property(x => x.Id)
            .HasConversion(
                IdentityEfConversions
                    .IdConverter<AuthSessionId>());

        builder.Property(x => x.UserId)
            .HasConversion(
                IdentityEfConversions
                    .IdConverter<ApplicationUserId>())
            .IsRequired();

        builder.Property(x => x.DeviceId)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.Generation)
            .IsRequired();

        builder.Property(x => x.CurrentSecretHash)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.PreviousGeneration);

        builder.Property(x => x.PreviousSecretHash)
            .HasMaxLength(128);

        builder.Property(x => x.PreviousRotatedAt);

        builder.Property(x => x.StartedAt)
            .IsRequired();

        builder.Property(x => x.ExpiresAt)
            .IsRequired();

        builder.Property(x => x.AbsoluteExpiresAt)
            .IsRequired();

        builder.Property(x => x.RevokedAt);

        builder.Property(x => x.RevocationReason)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.HasIndex(x => x.UserId);

        builder.HasIndex(x => x.ExpiresAt);

        builder.Property(x => x.Version)
            .HasColumnName("aggregate_version")
            .IsConcurrencyToken();

        IdentityEfConversions.ConfigureAudit(builder);
    }
}
