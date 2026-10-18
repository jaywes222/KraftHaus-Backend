using Commerce.Domain.Menus;
using Commerce.Domain.Common;
using Commerce.Domain.Krafts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Commerce.Infrastructure.Persistence.Configurations;

public sealed class KraftConfiguration : IEntityTypeConfiguration<Kraft>
{
    public const string SlugIndex = "ux_kraft_slug";

    public void Configure(EntityTypeBuilder<Kraft> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("kraft");
        builder.Ignore(x => x.Events);

        CommerceEf.ConfigureKey<Kraft, KraftId>(builder);

        builder.Property(x => x.Name).HasMaxLength(Kraft.NameMaxLength).IsRequired();
        builder.Property(x => x.Slug).HasMaxLength(Slug.MaxLength + 8).IsRequired();
        builder.Property(x => x.Discipline).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(Kraft.DescriptionMaxLength);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.LogoPath).HasMaxLength(MenuItem.ImagePathMaxLength);
        builder.Property(x => x.LogoVersion).IsRequired();

        builder.HasIndex(x => x.Slug).IsUnique().HasDatabaseName(SlugIndex);
        builder.HasIndex(x => new { x.Status, x.Name });

        builder.HasMany(x => x.Brands).WithOne().HasForeignKey("KraftId").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Brands).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();

        builder.HasMany(x => x.Krafters).WithOne().HasForeignKey("KraftId").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Krafters).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();

        builder.HasMany(x => x.Participations).WithOne().HasForeignKey("KraftId").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Participations).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();

        CommerceEf.ConfigureAudit(builder);
        CommerceEf.ConfigureAggregateVersion(builder);
    }
}

public sealed class KraftBrandConfiguration : IEntityTypeConfiguration<KraftBrand>
{
    public void Configure(EntityTypeBuilder<KraftBrand> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("kraft_brand");
        CommerceEf.ConfigureKey<KraftBrand, KraftBrandId>(builder);

        builder.Property(x => x.Name).HasMaxLength(KraftBrand.NameMaxLength).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(KraftBrand.DescriptionMaxLength);

        CommerceEf.ConfigureAudit(builder);
    }
}

public sealed class KrafterConfiguration : IEntityTypeConfiguration<Krafter>
{
    public void Configure(EntityTypeBuilder<Krafter> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("krafter");
        CommerceEf.ConfigureKey<Krafter, KrafterId>(builder);

        builder.Property(x => x.Name).HasMaxLength(Krafter.NameMaxLength).IsRequired();
        builder.Property(x => x.Role).HasMaxLength(Krafter.RoleMaxLength);
        builder.Property(x => x.Bio).HasMaxLength(Krafter.BioMaxLength);
        builder.Property(x => x.Instagram).HasMaxLength(Krafter.InstagramMaxLength);

        CommerceEf.ConfigureAudit(builder);
    }
}

public sealed class KraftParticipationConfiguration : IEntityTypeConfiguration<KraftParticipation>
{
    public void Configure(EntityTypeBuilder<KraftParticipation> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("kraft_event");
        CommerceEf.ConfigureKey<KraftParticipation, KraftParticipationId>(builder);

        builder.Property(x => x.EventId).IsRequired();
        builder.Property(x => x.CommissionPercent).HasColumnType("numeric(5,2)").IsRequired();
        builder.Property(x => x.Stall).HasMaxLength(KraftParticipation.StallMaxLength);
        builder.Property(x => x.PaymentRouting).HasConversion<string>().HasMaxLength(16)
            .HasDefaultValue(PaymentRouting.OwnTill).HasSentinel(PaymentRouting.OwnTill).IsRequired();
        builder.Ignore(x => x.PaysIntoPlatformTill);

        builder.HasIndex("KraftId", nameof(KraftParticipation.EventId)).IsUnique();
        builder.HasIndex(x => x.EventId);

        CommerceEf.ConfigureAudit(builder);
    }
}
