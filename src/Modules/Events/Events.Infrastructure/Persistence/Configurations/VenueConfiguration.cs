using Events.Domain.Common;
using Events.Domain.Venues;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Events.Infrastructure.Persistence.Configurations;

public sealed class VenueConfiguration : IEntityTypeConfiguration<Venue>
{
    public void Configure(EntityTypeBuilder<Venue> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("venue");

        EventsEf.ConfigureKey<Venue, VenueId>(builder);

        builder.Ignore(x => x.Events);

        builder.OwnsOne(x => x.Details, details => EventsEf.ConfigureVenueDetails(details, prefix: string.Empty));
        builder.Navigation(x => x.Details).IsRequired();

        builder.Property(x => x.Capacity);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.HasMany(x => x.Entrances)
            .WithOne()
            .HasForeignKey("VenueId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Entrances)
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .AutoInclude();

        EventsEf.ConfigureAggregateVersion(builder);
        EventsEf.ConfigureAudit(builder);
    }
}

public sealed class VenueEntranceConfiguration : IEntityTypeConfiguration<VenueEntrance>
{
    public void Configure(EntityTypeBuilder<VenueEntrance> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("venue_entrance");

        EventsEf.ConfigureKey<VenueEntrance, VenueEntranceId>(builder);

        builder.Property(x => x.Name).HasMaxLength(EntranceRules.NameMaxLength).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(EntranceRules.DescriptionMaxLength);

        EventsEf.ConfigureAudit(builder);
    }
}
