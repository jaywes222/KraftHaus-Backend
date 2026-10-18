using Common.Domain.StronglyTypedIds;
using Common.Infrastructure.Persistence.ValueConverters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Ticketing.Domain.Common;
using Ticketing.Domain.Waitlist;

namespace Ticketing.Infrastructure.Persistence.Configurations;

public sealed class WaitlistEntryConfiguration : IEntityTypeConfiguration<WaitlistEntry>
{
    /// <summary>One phone per event. The unit of work maps a violation of this index to <c>WaitlistAlreadyJoined</c>.</summary>
    public const string EventPhoneIndex = "ux_waitlist_entry_event_phone";

    private static readonly ValueConverter<ApplicationUserId?, Guid?> UserIdConverter = new(
        id => id.HasValue ? id.Value.Value : null,
        value => value.HasValue ? new ApplicationUserId(value.Value) : null);

    public void Configure(EntityTypeBuilder<WaitlistEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("waitlist_entry");

        builder.Ignore(x => x.Events);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasConversion(new StronglyTypedIdValueConverter<WaitlistEntryId>())
            .ValueGeneratedNever();

        builder.Property(x => x.EventId).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(WaitlistEntry.NameMaxLength).IsRequired();
        builder.Property(x => x.Phone).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Age).IsRequired();
        builder.Property(x => x.Gender).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(x => x.Location).HasMaxLength(WaitlistEntry.LocationMaxLength).IsRequired();
        builder.Property(x => x.Email).HasMaxLength(WaitlistEntry.EmailMaxLength);
        builder.Property(x => x.PrivacyPolicyVersion).HasMaxLength(WaitlistEntry.PolicyVersionMaxLength).IsRequired();
        builder.Property(x => x.PrivacyAcceptedAt).IsRequired();
        builder.Property(x => x.MarketingConsent).IsRequired();
        builder.Property(x => x.JoinedAt).IsRequired();
        builder.Property(x => x.ConfirmationSentAt);
        builder.Property(x => x.ConfirmationAttempts).HasDefaultValue(0).IsRequired();

        builder.HasIndex(x => new { x.EventId, x.Phone })
            .IsUnique()
            .HasDatabaseName(EventPhoneIndex);

        // Early access looks a phone up across the tenant's events; exports list an event's entries in join order.
        builder.HasIndex(x => x.Phone);
        builder.HasIndex(x => new { x.EventId, x.JoinedAt });

        builder.Property(x => x.Version)
            .HasColumnName("aggregate_version")
            .IsConcurrencyToken();

        builder.Property(x => x.CreatedOn).IsRequired();
        builder.Property(x => x.CreatedBy).HasConversion(UserIdConverter);
        builder.Property(x => x.LastModifiedOn);
        builder.Property(x => x.LastModifiedBy).HasConversion(UserIdConverter);
    }
}
