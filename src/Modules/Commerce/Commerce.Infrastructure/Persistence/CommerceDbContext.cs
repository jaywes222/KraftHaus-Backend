using Commerce.Domain.Settlements;
using Commerce.Domain.Krafts;
using Commerce.Domain.Menus;
using Commerce.Domain.Orders;
using Commerce.Infrastructure.Auditing;
using Commerce.Infrastructure.Persistence.Configurations;
using Common.Infrastructure.Messaging;
using Common.Infrastructure.Persistence.ValueConverters;
using Microsoft.EntityFrameworkCore;

namespace Commerce.Infrastructure.Persistence;

/// <summary>
///     The Commerce module's tables (schema <c>commerce</c>) in each <b>tenant</b> database (Spec 08 §5.1, TD-04):
///     Krafts, brands, Krafters and event terms, menus, stock, and food and drink orders.
/// </summary>
public sealed class CommerceDbContext(DbContextOptions<CommerceDbContext> options) : DbContext(options)
{
    public const string Schema = "commerce";
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public DbSet<Kraft> Krafts => Set<Kraft>();

    public DbSet<MenuItem> MenuItems => Set<MenuItem>();

    public DbSet<KraftOrder> Orders => Set<KraftOrder>();

    public DbSet<KraftSettlement> Settlements => Set<KraftSettlement>();

    /// <summary>Append-only audit trail of Commerce changes. Written by the audit interceptor only.</summary>
    public DbSet<CommerceAuditRecord> AuditLog => Set<CommerceAuditRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.ApplyConfiguration(new KraftConfiguration());
        modelBuilder.ApplyConfiguration(new KraftBrandConfiguration());
        modelBuilder.ApplyConfiguration(new KrafterConfiguration());
        modelBuilder.ApplyConfiguration(new KraftParticipationConfiguration());
        modelBuilder.ApplyConfiguration(new MenuItemConfiguration());
        modelBuilder.ApplyConfiguration(new StockBucketConfiguration());
        modelBuilder.ApplyConfiguration(new StockHoldConfiguration());
        modelBuilder.ApplyConfiguration(new KraftOrderConfiguration());
        modelBuilder.ApplyConfiguration(new KraftOrderLineConfiguration());
        modelBuilder.ApplyConfiguration(new KraftSettlementConfiguration());
        modelBuilder.ApplyConfiguration(new CommerceAuditRecordConfiguration());
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
