using Common.Infrastructure.Messaging;
using Common.Infrastructure.Persistence.ValueConverters;
using Microsoft.EntityFrameworkCore;
using Ticketing.Domain.Orders;
using Ticketing.Domain.Tickets;
using Ticketing.Domain.Waitlist;
using Ticketing.Infrastructure.Auditing;
using Ticketing.Infrastructure.Persistence.Configurations;

namespace Ticketing.Infrastructure.Persistence;

/// <summary>
///     The Ticketing module's tables (schema <c>ticketing</c>) in each <b>tenant</b> database (Spec 08 §5.1, TD-04):
///     the waitlist today; capacity buckets, holds, orders and tickets as S2/S3 land.
/// </summary>
public sealed class TicketingDbContext(DbContextOptions<TicketingDbContext> options) : DbContext(options)
{
    public const string Schema = "ticketing";
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public DbSet<WaitlistEntry> WaitlistEntries => Set<WaitlistEntry>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<Ticket> Tickets => Set<Ticket>();

    /// <summary>Append-only audit trail of Ticketing changes. Written by the audit interceptor only.</summary>
    public DbSet<TicketingAuditRecord> AuditLog => Set<TicketingAuditRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.ApplyConfiguration(new WaitlistEntryConfiguration());
        modelBuilder.ApplyConfiguration(new OrderConfiguration());
        modelBuilder.ApplyConfiguration(new OrderLineConfiguration());
        modelBuilder.ApplyConfiguration(new TicketConfiguration());
        modelBuilder.ApplyConfiguration(new CapacityBucketConfiguration());
        modelBuilder.ApplyConfiguration(new CapacityHoldConfiguration());
        modelBuilder.ApplyConfiguration(new TicketingAuditRecordConfiguration());
        modelBuilder.AddOutbox();
        modelBuilder.AddProcessedMessages();

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
