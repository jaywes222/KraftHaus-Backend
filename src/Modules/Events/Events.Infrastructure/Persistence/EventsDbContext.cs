using Common.Infrastructure.Persistence.ValueConverters;
using Events.Domain.Model;
using Events.Domain.Venues;
using Events.Infrastructure.Auditing;
using Events.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Events.Infrastructure.Persistence;

/// <summary>
///     The Events module's tables (schema <c>events</c>) in each <b>tenant</b> database (Spec 08 §5.1, TD-04). The
///     connection is the resolved tenant's database; there is no Events data in the platform database.
/// </summary>
public sealed class EventsDbContext(DbContextOptions<EventsDbContext> options) : DbContext(options)
{
    public const string Schema = "events";
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public DbSet<Event> Events => Set<Event>();

    public DbSet<Venue> Venues => Set<Venue>();

    /// <summary>Append-only audit trail of Events changes. Written by the audit interceptor only.</summary>
    public DbSet<EventsAuditRecord> AuditLog => Set<EventsAuditRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.ApplyConfiguration(new VenueConfiguration());
        modelBuilder.ApplyConfiguration(new VenueEntranceConfiguration());
        modelBuilder.ApplyConfiguration(new EventConfiguration());
        modelBuilder.ApplyConfiguration(new EventEntranceConfiguration());
        modelBuilder.ApplyConfiguration(new EventDateConfiguration());
        modelBuilder.ApplyConfiguration(new TicketTierConfiguration());
        modelBuilder.ApplyConfiguration(new SalePhaseConfiguration());
        modelBuilder.ApplyConfiguration(new GroupOfferConfiguration());
        modelBuilder.ApplyConfiguration(new EventsAuditRecordConfiguration());

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
