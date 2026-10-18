using Displays.Domain.Gallery;
using Common.Infrastructure.Persistence.ValueConverters;
using Displays.Domain.Assets;
using Displays.Domain.Configurations;
using Displays.Domain.Devices;
using Displays.Domain.Programme;
using Displays.Infrastructure.Auditing;
using Displays.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Displays.Infrastructure.Persistence;

/// <summary>
///     The Displays module's tables (schema <c>displays</c>) in each <b>tenant</b> database (Spec 08 §5.1, TD-04):
///     paired screens and pairing requests, configurations, the programme and uploaded images.
/// </summary>
public sealed class DisplaysDbContext(DbContextOptions<DisplaysDbContext> options) : DbContext(options)
{
    public const string Schema = "displays";
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public DbSet<DisplayDevice> Devices => Set<DisplayDevice>();

    public DbSet<DisplayPairing> Pairings => Set<DisplayPairing>();

    public DbSet<DisplayConfiguration> Configurations => Set<DisplayConfiguration>();

    public DbSet<ProgrammeSlot> ProgrammeSlots => Set<ProgrammeSlot>();

    public DbSet<DisplayAsset> Assets => Set<DisplayAsset>();

    public DbSet<GalleryPhoto> Gallery => Set<GalleryPhoto>();

    /// <summary>Append-only audit trail of Displays changes. Written by the audit interceptor only.</summary>
    public DbSet<DisplaysAuditRecord> AuditLog => Set<DisplaysAuditRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.ApplyConfiguration(new DisplayDeviceConfiguration());
        modelBuilder.ApplyConfiguration(new DisplayPairingConfiguration());
        modelBuilder.ApplyConfiguration(new DisplayConfigurationConfiguration());
        modelBuilder.ApplyConfiguration(new ProgrammeSlotConfiguration());
        modelBuilder.ApplyConfiguration(new DisplayAssetConfiguration());
        modelBuilder.ApplyConfiguration(new GalleryPhotoConfiguration());
        modelBuilder.ApplyConfiguration(new DisplaysAuditRecordConfiguration());

        base.OnModelCreating(modelBuilder);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        configurationBuilder
            .Properties<DateTimeOffset>()
            .HaveConversion<UtcDateTimeOffsetConverter>();

        base.ConfigureConventions(configurationBuilder);
    }
}
