using Common.Infrastructure.Persistence.ValueConverters;
using Events.Domain.Common;
using Events.Domain.Model;
using Events.Domain.Venues;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Events.Infrastructure.Persistence.Configurations;

public sealed class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("event");

        EventsEf.ConfigureKey<Event, EventId>(builder);

        builder.Ignore(x => x.Events);

        builder.Property(x => x.Slug).HasMaxLength(Event.SlugMaxLength).IsRequired();
        builder.HasIndex(x => x.Slug).IsUnique();

        builder.Property(x => x.Title).HasMaxLength(Event.TitleMaxLength).IsRequired();
        builder.Property(x => x.Summary).HasMaxLength(Event.SummaryMaxLength);
        builder.Property(x => x.Description).HasMaxLength(Event.DescriptionMaxLength);
        builder.Property(x => x.PosterUrl).HasMaxLength(2048);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.HasIndex(x => x.Status);

        builder.Property(x => x.PublishedAt);
        builder.Property(x => x.CancelledAt);
        builder.Property(x => x.CancellationReason).HasMaxLength(Event.CancellationReasonMaxLength);
        builder.Property(x => x.CompletedAt);

        // The event's own venue copy (optional until set), one row per event in its own table: an optional owned
        // value cannot share the event row when its only required columns live in a nested owned value.
        builder.OwnsOne(x => x.Venue, venue =>
        {
            venue.ToTable("event_venue");
            venue.WithOwner().HasForeignKey("EventId");
            venue.HasKey("EventId");

            venue.Property(x => x.SourceVenueId)
                .HasColumnName("SourceVenueId")
                .HasConversion(new StronglyTypedIdValueConverter<VenueId>());

            venue.OwnsOne(x => x.Details, details => EventsEf.ConfigureVenueDetails(details, prefix: string.Empty));
            venue.Navigation(x => x.Details).IsRequired();
        });

        builder.OwnsOne(x => x.Policies, policies =>
        {
            policies.Property(x => x.RefundsEnabled).HasColumnName("RefundsEnabled");
            policies.Property(x => x.TransfersEnabled).HasColumnName("TransfersEnabled");
            policies.Property(x => x.MaxTransfersPerTicket).HasColumnName("MaxTransfersPerTicket");
            policies.Property(x => x.LipaMdogoMdogoEnabled).HasColumnName("LipaMdogoMdogoEnabled");
            policies.Property(x => x.HoldMinutes).HasColumnName("HoldMinutes");
            policies.Property(x => x.MaxTicketsPerOrder).HasColumnName("MaxTicketsPerOrder");
        });
        builder.Navigation(x => x.Policies).IsRequired();

        builder.HasMany(x => x.Entrances)
            .WithOne()
            .HasForeignKey("EventId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Entrances).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(x => x.Dates)
            .WithOne()
            .HasForeignKey("EventId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Dates).UsePropertyAccessMode(PropertyAccessMode.Field);

        EventsEf.ConfigureAggregateVersion(builder);
        EventsEf.ConfigureAudit(builder);
    }
}

public sealed class EventEntranceConfiguration : IEntityTypeConfiguration<EventEntrance>
{
    public void Configure(EntityTypeBuilder<EventEntrance> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("event_entrance");

        EventsEf.ConfigureKey<EventEntrance, EventEntranceId>(builder);

        builder.Property(x => x.Name).HasMaxLength(EntranceRules.NameMaxLength).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(EntranceRules.DescriptionMaxLength);

        EventsEf.ConfigureAudit(builder);
    }
}

public sealed class EventDateConfiguration : IEntityTypeConfiguration<EventDate>
{
    public void Configure(EntityTypeBuilder<EventDate> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("event_date");

        EventsEf.ConfigureKey<EventDate, EventDateId>(builder);

        builder.Property(x => x.Label).HasMaxLength(EventDate.LabelMaxLength);
        builder.Property(x => x.StartsAt).IsRequired();
        builder.Property(x => x.EndsAt).IsRequired();
        builder.Property(x => x.DoorsOpenAt);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.HasIndex(x => x.StartsAt);

        builder.HasMany(x => x.Tiers)
            .WithOne()
            .HasForeignKey("EventDateId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Tiers).UsePropertyAccessMode(PropertyAccessMode.Field);

        EventsEf.ConfigureAudit(builder);
    }
}

public sealed class TicketTierConfiguration : IEntityTypeConfiguration<TicketTier>
{
    public void Configure(EntityTypeBuilder<TicketTier> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ticket_tier", t => t.HasCheckConstraint("CK_ticket_tier_capacity", "\"Capacity\" > 0"));

        EventsEf.ConfigureKey<TicketTier, TicketTierId>(builder);

        builder.Property(x => x.Name).HasMaxLength(TicketTier.NameMaxLength).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(TicketTier.DescriptionMaxLength);
        builder.Property(x => x.Capacity).IsRequired();
        builder.Property(x => x.Position).IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.HasMany(x => x.Phases)
            .WithOne()
            .HasForeignKey("TicketTierId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Phases).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(x => x.Offers)
            .WithOne()
            .HasForeignKey("TicketTierId")
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(x => x.Offers).UsePropertyAccessMode(PropertyAccessMode.Field);

        EventsEf.ConfigureAudit(builder);
    }
}

public sealed class SalePhaseConfiguration : IEntityTypeConfiguration<SalePhase>
{
    public void Configure(EntityTypeBuilder<SalePhase> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("sale_phase", t =>
        {
            t.HasCheckConstraint("CK_sale_phase_window", "\"EndsAt\" > \"StartsAt\"");
            t.HasCheckConstraint("CK_sale_phase_price", "\"PriceAmount\" > 0");
        });

        EventsEf.ConfigureKey<SalePhase, SalePhaseId>(builder);

        builder.Ignore(x => x.Price);
        builder.Ignore(x => x.Window);

        builder.Property(x => x.Name).HasMaxLength(SalePhase.NameMaxLength).IsRequired();
        builder.Property(x => x.PriceAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.PriceCurrency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(x => x.StartsAt).IsRequired();
        builder.Property(x => x.EndsAt).IsRequired();
        builder.Property(x => x.Allocation);
        builder.Property(x => x.EarlyAccess).HasDefaultValue(false).IsRequired();

        EventsEf.ConfigureAudit(builder);
    }
}

public sealed class GroupOfferConfiguration : IEntityTypeConfiguration<GroupOffer>
{
    public void Configure(EntityTypeBuilder<GroupOffer> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("group_offer", t =>
        {
            t.HasCheckConstraint("CK_group_offer_window", "\"EndsAt\" > \"StartsAt\"");
            t.HasCheckConstraint("CK_group_offer_price", "\"PriceAmount\" > 0");
            t.HasCheckConstraint("CK_group_offer_admits", "\"Admits\" >= 2");
        });

        EventsEf.ConfigureKey<GroupOffer, GroupOfferId>(builder);

        builder.Ignore(x => x.Price);
        builder.Ignore(x => x.Window);

        builder.Property(x => x.Kind)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(x => x.Name).HasMaxLength(GroupOffer.NameMaxLength).IsRequired();
        builder.Property(x => x.Admits).IsRequired();
        builder.Property(x => x.PriceAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.PriceCurrency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(x => x.StartsAt).IsRequired();
        builder.Property(x => x.EndsAt).IsRequired();
        builder.Property(x => x.Allocation);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        EventsEf.ConfigureAudit(builder);
    }
}
