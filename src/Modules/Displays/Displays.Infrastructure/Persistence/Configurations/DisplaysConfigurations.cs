using Displays.Domain.Gallery;
using System.Text.Json;
using Common.Infrastructure.Persistence.ValueConverters;
using Displays.Domain.Assets;
using Displays.Domain.Common;
using Displays.Domain.Configurations;
using Displays.Domain.Devices;
using Displays.Domain.Programme;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Displays.Infrastructure.Persistence.Configurations;

/// <summary>Small value lists (slides, schedule entries) stored as jsonb on their aggregate, replaced whole on save.</summary>
internal static class JsonList
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static ValueConverter<IReadOnlyList<T>, string> Converter<T>() =>
        new(
            value => JsonSerializer.Serialize(value, Options),
            json => JsonSerializer.Deserialize<List<T>>(json, Options) ?? new List<T>());

    public static ValueComparer<IReadOnlyList<T>> Comparer<T>() =>
        new(
            (left, right) => JsonSerializer.Serialize(left, Options) == JsonSerializer.Serialize(right, Options),
            value => JsonSerializer.Serialize(value, Options).GetHashCode(StringComparison.Ordinal),
            value => JsonSerializer.Deserialize<List<T>>(JsonSerializer.Serialize(value, Options), Options)!);
}

public sealed class DisplayDeviceConfiguration : IEntityTypeConfiguration<DisplayDevice>
{
    public const string TokenHashIndex = "ux_display_device_token_hash";

    public void Configure(EntityTypeBuilder<DisplayDevice> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("display_device");
        builder.Ignore(x => x.Events);
        DisplaysEf.ConfigureKey<DisplayDevice, DisplayDeviceId>(builder);

        builder.Property(x => x.Name).HasMaxLength(DisplayDevice.NameMaxLength).IsRequired();
        builder.Property(x => x.Location).HasMaxLength(DisplayDevice.LocationMaxLength);
        builder.Property(x => x.EventId).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.RevokeReason).HasMaxLength(DisplayDevice.RevokeReasonMaxLength);
        builder.Property(x => x.Schedule)
            .HasField("_schedule")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnType("jsonb")
            .HasConversion(JsonList.Converter<DisplayScheduleEntry>(), JsonList.Comparer<DisplayScheduleEntry>())
            .IsRequired();

        builder.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName(TokenHashIndex);
        builder.HasIndex(x => new { x.EventId, x.Status });

        DisplaysEf.ConfigureAudit(builder);
        DisplaysEf.ConfigureAggregateVersion(builder);
    }
}

public sealed class DisplayPairingConfiguration : IEntityTypeConfiguration<DisplayPairing>
{
    public void Configure(EntityTypeBuilder<DisplayPairing> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("display_pairing");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(new StronglyTypedIdValueConverter<DisplayPairingId>()).ValueGeneratedNever();
        builder.Property(x => x.Code).HasMaxLength(DisplayPairing.CodeLength).IsRequired();
        builder.Property(x => x.SecretHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.PendingToken).HasMaxLength(1024);

        builder.HasIndex(x => new { x.Code, x.Status });
        builder.HasIndex(x => x.ExpiresAt);
    }
}

public sealed class DisplayConfigurationConfiguration : IEntityTypeConfiguration<DisplayConfiguration>
{
    public void Configure(EntityTypeBuilder<DisplayConfiguration> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("display_configuration");
        builder.Ignore(x => x.Events);
        DisplaysEf.ConfigureKey<DisplayConfiguration, DisplayConfigurationId>(builder);

        builder.Property(x => x.EventId).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(DisplayConfiguration.NameMaxLength).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.Slides)
            .HasField("_slides")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .HasColumnType("jsonb")
            .HasConversion(JsonList.Converter<DisplaySlide>(), JsonList.Comparer<DisplaySlide>())
            .IsRequired();

        builder.HasIndex(x => new { x.EventId, x.Status });

        DisplaysEf.ConfigureAudit(builder);
        DisplaysEf.ConfigureAggregateVersion(builder);
    }
}

public sealed class ProgrammeSlotConfiguration : IEntityTypeConfiguration<ProgrammeSlot>
{
    public void Configure(EntityTypeBuilder<ProgrammeSlot> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("programme_slot");
        builder.Ignore(x => x.Events);
        DisplaysEf.ConfigureKey<ProgrammeSlot, ProgrammeSlotId>(builder);

        builder.Property(x => x.EventId).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(ProgrammeSlot.TitleMaxLength).IsRequired();
        builder.Property(x => x.Who).HasMaxLength(ProgrammeSlot.WhoMaxLength);
        builder.Property(x => x.Stage).HasMaxLength(ProgrammeSlot.StageMaxLength);
        builder.Property(x => x.Description).HasMaxLength(ProgrammeSlot.DescriptionMaxLength);

        builder.HasIndex(x => new { x.EventId, x.StartsAt });

        DisplaysEf.ConfigureAudit(builder);
        DisplaysEf.ConfigureAggregateVersion(builder);
    }
}

public sealed class DisplayAssetConfiguration : IEntityTypeConfiguration<DisplayAsset>
{
    public void Configure(EntityTypeBuilder<DisplayAsset> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("display_asset");
        builder.Ignore(x => x.Events);
        DisplaysEf.ConfigureKey<DisplayAsset, DisplayAssetId>(builder);

        builder.Property(x => x.FileName).HasMaxLength(DisplayAsset.FileNameMaxLength).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(DisplayAsset.ContentTypeMaxLength).IsRequired();
        builder.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
        builder.Property(x => x.StoragePath).HasMaxLength(DisplayAsset.StoragePathMaxLength).IsRequired();

        builder.HasIndex(x => x.UploadedAt).IsDescending();

        DisplaysEf.ConfigureAudit(builder);
        DisplaysEf.ConfigureAggregateVersion(builder);
    }
}

public sealed class GalleryPhotoConfiguration : IEntityTypeConfiguration<GalleryPhoto>
{
    public void Configure(EntityTypeBuilder<GalleryPhoto> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("gallery_photo");
        builder.Ignore(x => x.Events);
        DisplaysEf.ConfigureKey<GalleryPhoto, GalleryPhotoId>(builder);

        builder.Property(x => x.EventId).IsRequired();
        builder.Property(x => x.AssetId).IsRequired();
        builder.Property(x => x.Caption).HasMaxLength(GalleryPhoto.CaptionMaxLength);
        builder.Property(x => x.SortOrder).IsRequired();
        builder.Property(x => x.AddedAt).IsRequired();

        builder.HasIndex(x => new { x.EventId, x.SortOrder });
        builder.HasIndex(x => x.AssetId);

        DisplaysEf.ConfigureAudit(builder);
        DisplaysEf.ConfigureAggregateVersion(builder);
    }
}
