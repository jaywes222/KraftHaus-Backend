using Common.Infrastructure.Persistence.ValueConverters;
using Communications.Domain.Broadcasts;
using Communications.Domain.Messages;
using Communications.Domain.OptOuts;
using Communications.Domain.Templates;
using Communications.Infrastructure.Auditing;
using Communications.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Communications.Infrastructure.Persistence;

/// <summary>
///     The Communications module's tables (schema <c>communications</c>) in each <b>tenant</b> database: the message
///     log, broadcasts, message templates and marketing opt-outs.
/// </summary>
public sealed class CommunicationsDbContext(DbContextOptions<CommunicationsDbContext> options) : DbContext(options)
{
    public const string Schema = "communications";
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public DbSet<Message> Messages => Set<Message>();

    public DbSet<Broadcast> Broadcasts => Set<Broadcast>();

    public DbSet<OptOut> OptOuts => Set<OptOut>();

    public DbSet<MessageTemplate> Templates => Set<MessageTemplate>();

    /// <summary>Append-only audit trail of Communications changes. Written by the audit interceptor only.</summary>
    public DbSet<CommunicationsAuditRecord> AuditLog => Set<CommunicationsAuditRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.ApplyConfiguration(new MessageConfiguration());
        modelBuilder.ApplyConfiguration(new BroadcastConfiguration());
        modelBuilder.ApplyConfiguration(new OptOutConfiguration());
        modelBuilder.ApplyConfiguration(new MessageTemplateConfiguration());
        modelBuilder.ApplyConfiguration(new CommunicationsAuditRecordConfiguration());

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
