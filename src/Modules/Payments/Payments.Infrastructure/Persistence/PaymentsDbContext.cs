using Common.Infrastructure.Messaging;
using Common.Infrastructure.Persistence.ValueConverters;
using Microsoft.EntityFrameworkCore;
using Payments.Domain.Destinations;
using Payments.Domain.Payments;
using Payments.Infrastructure.Auditing;
using Payments.Infrastructure.Persistence.Configurations;

namespace Payments.Infrastructure.Persistence;

/// <summary>The Payments module's tables (schema <c>payments</c>) in each tenant database: payments, tills, settlement records, audit log, outbox and processed messages.</summary>
public sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : DbContext(options)
{
    public const string Schema = "payments";
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<SettlementRecord> Settlements => Set<SettlementRecord>();

    public DbSet<PaymentDestination> Destinations => Set<PaymentDestination>();

    public DbSet<PaymentsAuditRecord> AuditLog => Set<PaymentsAuditRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.ApplyConfiguration(new PaymentConfiguration());
        modelBuilder.ApplyConfiguration(new SettlementRecordConfiguration());
        modelBuilder.ApplyConfiguration(new PaymentDestinationConfiguration());
        modelBuilder.ApplyConfiguration(new PaymentsAuditRecordConfiguration());
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
