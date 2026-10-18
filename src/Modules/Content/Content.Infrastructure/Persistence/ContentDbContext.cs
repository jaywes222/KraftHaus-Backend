using Common.Infrastructure.Persistence.ValueConverters;
using Content.Domain;
using Content.Infrastructure.Auditing;
using Content.Infrastructure.Persistence.Configurations;
using Microsoft.EntityFrameworkCore;

namespace Content.Infrastructure.Persistence;

/// <summary>The Content module's tables (schema <c>content</c>) in each <b>tenant</b> database: articles and Web Studio blocks.</summary>
public sealed class ContentDbContext(DbContextOptions<ContentDbContext> options) : DbContext(options)
{
    public const string Schema = "content";
    public const string MigrationsHistoryTable = "__ef_migrations_history";
    public const string SlugIndex = "ux_article_slug";

    public DbSet<Article> Articles => Set<Article>();

    public DbSet<SiteBlock> SiteBlocks => Set<SiteBlock>();

    public DbSet<ContentAuditRecord> AuditLog => Set<ContentAuditRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<Article>(builder =>
        {
            builder.ToTable("article");
            builder.Ignore(x => x.Events);
            builder.Ignore(x => x.Tags);
            ContentEf.ConfigureKey<Article, ArticleId>(builder);

            builder.Property(x => x.Slug).HasMaxLength(Article.SlugMaxLength).IsRequired();
            builder.Property(x => x.Title).HasMaxLength(Article.TitleMaxLength).IsRequired();
            builder.Property(x => x.Summary).HasMaxLength(Article.SummaryMaxLength);
            builder.Property(x => x.Body).HasMaxLength(Article.BodyMaxLength).IsRequired();
            builder.Property(x => x.AuthorName).HasMaxLength(Article.AuthorMaxLength);
            builder.Property<string>("_tags").HasColumnName("Tags").HasMaxLength(Article.MaxTags * (Article.TagMaxLength + 1) + 1).IsRequired();
            builder.Property(x => x.CoverImage).HasMaxLength(Article.CoverMaxLength);
            builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();

            builder.HasIndex(x => x.Slug).HasDatabaseName(SlugIndex).IsUnique();
            builder.HasIndex(x => new { x.Status, x.PublishedAt });

            ContentEf.ConfigureAudit(builder);
            ContentEf.ConfigureAggregateVersion(builder);
        });

        modelBuilder.Entity<SiteBlock>(builder =>
        {
            builder.ToTable("site_block");
            builder.Ignore(x => x.Events);
            builder.Ignore(x => x.HasUnpublishedChanges);
            ContentEf.ConfigureKey<SiteBlock, SiteBlockId>(builder);

            builder.Property(x => x.Key).HasConversion<string>().HasMaxLength(32).IsRequired();
            builder.Property(x => x.Draft).HasColumnType("jsonb").IsRequired();
            builder.Property(x => x.Published).HasColumnType("jsonb");

            builder.HasIndex(x => x.Key).IsUnique();

            ContentEf.ConfigureAudit(builder);
            ContentEf.ConfigureAggregateVersion(builder);
        });

        modelBuilder.ApplyConfiguration(new ContentAuditRecordConfiguration());

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
