using Common.Domain.StronglyTypedIds;
using Identity.Domain.Badges;
using Identity.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Identity.Infrastructure.Persistence.Configurations;

public sealed class StaffBadgeConfiguration : IEntityTypeConfiguration<StaffBadge>
{
    public void Configure(EntityTypeBuilder<StaffBadge> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("staff_badge");
        builder.HasKey(x => x.Id);
        builder.Ignore(x => x.Events);

        builder.Property(x => x.Id).HasConversion(IdentityEfConversions.IdConverter<StaffBadgeId>()).ValueGeneratedNever();
        builder.Property(x => x.TenantId).HasConversion(IdentityEfConversions.TenantIdConverter()).IsRequired();
        builder.Property(x => x.UserId).HasConversion(IdentityEfConversions.IdConverter<ApplicationUserId>()).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(StaffBadge.CodeLength).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(StaffBadge.TitleMaxLength);
        builder.Property(x => x.PhotoPath).HasMaxLength(StaffBadge.PhotoPathMaxLength);
        builder.Property(x => x.PhotoContentType).HasMaxLength(32);
        builder.Property(x => x.IssuedAt).IsRequired();

        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();

        builder.Property(x => x.Version)
            .HasColumnName("aggregate_version")
            .IsConcurrencyToken();

        IdentityEfConversions.ConfigureAudit(builder);
    }
}
