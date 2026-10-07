using Common.Domain.StronglyTypedIds;
using Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Identity.Infrastructure.Persistence;

public sealed class ConsentRecordConfiguration
    : IEntityTypeConfiguration<ConsentRecord>
{
    public void Configure(
        EntityTypeBuilder<ConsentRecord> builder)
    {
        builder.ToTable("user_consent");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasConversion(
                IdentityEfConversions
                    .IdConverter<ConsentRecordId>());

        builder.Property(x => x.UserId)
            .HasConversion(
                IdentityEfConversions
                    .IdConverter<ApplicationUserId>())
            .IsRequired();

        builder.Property(x => x.Kind)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(x => x.Granted)
            .IsRequired();

        builder.Property(x => x.PolicyKey)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.PolicyVersion)
            .IsRequired();

        builder.Property(x => x.PolicyTextHash)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(x => x.RecordedAt)
            .IsRequired();

        IdentityEfConversions.ConfigureAudit(builder);

        builder.HasIndex(
                x => new
                {
                    x.UserId,
                    x.Kind,
                    x.RecordedAt
                });
    }
}
