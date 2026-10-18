using Common.Infrastructure.Persistence.ValueConverters;
using Microsoft.EntityFrameworkCore;
using Music.Domain.Requests;
using Music.Infrastructure.Auditing;
using Music.Infrastructure.Persistence.Configurations;

namespace Music.Infrastructure.Persistence;

/// <summary>
///     The Music module's tables (schema <c>music</c>) in each <b>tenant</b> database: an event's request board and
///     its song requests with the votes behind them.
/// </summary>
public sealed class MusicDbContext(DbContextOptions<MusicDbContext> options) : DbContext(options)
{
    public const string Schema = "music";
    public const string MigrationsHistoryTable = "__ef_migrations_history";

    public DbSet<RequestBoard> Boards => Set<RequestBoard>();

    public DbSet<SongRequest> Requests => Set<SongRequest>();

    public DbSet<SongRequestVote> Votes => Set<SongRequestVote>();

    /// <summary>Append-only audit trail of Music changes. Written by the audit interceptor only.</summary>
    public DbSet<MusicAuditRecord> AuditLog => Set<MusicAuditRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.ApplyConfiguration(new RequestBoardConfiguration());
        modelBuilder.ApplyConfiguration(new SongRequestConfiguration());
        modelBuilder.ApplyConfiguration(new SongRequestVoteConfiguration());
        modelBuilder.ApplyConfiguration(new MusicAuditRecordConfiguration());

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
