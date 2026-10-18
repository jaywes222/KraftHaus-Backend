using Common.Infrastructure.Persistence.ValueConverters;
using Feedback.Domain;
using Microsoft.EntityFrameworkCore;

namespace Feedback.Infrastructure.Persistence;

/// <summary>
///     The Feedback module's table (schema <c>feedback</c>) in each <b>tenant</b> database: anonymous ratings. No audit
///     log on purpose: an audit row carries the actor, which would undo the anonymity BR-027 promises.
/// </summary>
public sealed class FeedbackDbContext(DbContextOptions<FeedbackDbContext> options) : DbContext(options)
{
    public const string Schema = "feedback";
    public const string MigrationsHistoryTable = "__ef_migrations_history";
    public const string HashIndex = "ux_rating_dedupe_hash";

    public DbSet<Rating> Ratings => Set<Rating>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Rating>(builder =>
        {
            builder.ToTable("rating");
            builder.HasKey(x => x.Id);
            builder.Property(x => x.Id).ValueGeneratedNever();
            builder.Property(x => x.EventId).IsRequired();
            builder.Property(x => x.EventDateId).IsRequired();
            builder.Property(x => x.DedupeHash).HasMaxLength(Rating.HashLength).IsFixedLength().IsRequired();
            builder.Property(x => x.Overall).IsRequired();
            builder.Property(x => x.Comment).HasMaxLength(Rating.CommentMaxLength);
            builder.Property(x => x.SubmittedOn).IsRequired();

            // Optimistic concurrency on PostgreSQL's row version; kept out of the domain model.
            builder.Property<uint>("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();

            builder.HasIndex(x => x.DedupeHash).HasDatabaseName(HashIndex).IsUnique();
            builder.HasIndex(x => new { x.EventId, x.EventDateId });

            builder.ToTable(t =>
            {
                t.HasCheckConstraint("ck_rating_overall", "\"Overall\" BETWEEN 1 AND 5");
                t.HasCheckConstraint("ck_rating_aspects",
                    "(\"Music\" IS NULL OR \"Music\" BETWEEN 1 AND 5) AND (\"Food\" IS NULL OR \"Food\" BETWEEN 1 AND 5) AND " +
                    "(\"Venue\" IS NULL OR \"Venue\" BETWEEN 1 AND 5) AND (\"Organisation\" IS NULL OR \"Organisation\" BETWEEN 1 AND 5)");
            });
        });

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
