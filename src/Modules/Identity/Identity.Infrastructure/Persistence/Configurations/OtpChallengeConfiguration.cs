using Identity.Domain.Common;
using Identity.Domain.Otp;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Identity.Infrastructure.Persistence.Configurations;

public sealed class OtpChallengeConfiguration
    : IEntityTypeConfiguration<OtpChallenge>
{
    public void Configure(
        EntityTypeBuilder<OtpChallenge> builder)
    {
        builder.ToTable("otp_challenge");

        builder.HasKey(x => x.Id);

        builder.Ignore(x => x.Events);

        builder.Property(x => x.Id)
            .HasConversion(
                IdentityEfConversions
                    .IdConverter<OtpChallengeId>());

        builder.Property(x => x.Channel)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.Destination)
            .HasMaxLength(320)
            .IsRequired();

        builder.Property(x => x.Purpose)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.CodeHash)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.IssuedAt)
            .IsRequired();

        builder.Property(x => x.ExpiresAt)
            .IsRequired();

        builder.Property(x => x.AttemptCount)
            .IsRequired();

        builder.Property(x => x.VerifiedAt);

        builder.HasIndex(
                x => new
                {
                    x.Channel,
                    x.Destination,
                    x.Purpose,
                    x.IssuedAt
                })
            .IsDescending(false, false, false, true);

        builder.HasIndex(x => x.ExpiresAt);

        builder.Property(x => x.Version)
            .HasColumnName("aggregate_version")
            .IsConcurrencyToken();

        IdentityEfConversions.ConfigureAudit(builder);
    }
}
