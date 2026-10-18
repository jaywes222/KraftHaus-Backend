using System.Text.Json;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Commerce.Domain.Common;
using Commerce.Domain.Menus;
using Commerce.Infrastructure.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Commerce.Infrastructure.Persistence.Configurations;

public sealed class MenuItemConfiguration : IEntityTypeConfiguration<MenuItem>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public void Configure(EntityTypeBuilder<MenuItem> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("menu_item");
        builder.Ignore(x => x.Events);
        builder.Ignore(x => x.SoldLive);
        builder.Ignore(x => x.Reservable);
        builder.Ignore(x => x.IsOrderable);
        builder.Ignore(x => x.IsActivity);

        CommerceEf.ConfigureKey<MenuItem, MenuItemId>(builder);

        builder.Property(x => x.KraftId).IsRequired();
        builder.Property(x => x.EventId).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(MenuItem.NameMaxLength).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(MenuItem.DescriptionMaxLength);
        builder.Property(x => x.Category).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.Price).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.LiveStock);
        builder.Property(x => x.ReservationAllocation);
        builder.Property(x => x.AgeRestricted).IsRequired();
        builder.Property(x => x.SortOrder).IsRequired();
        builder.Property(x => x.Available).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.AddOns)
            .HasColumnType("jsonb")
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonOptions),
                v => (IReadOnlyList<MenuAddOn>?)JsonSerializer.Deserialize<MenuAddOn[]>(v, JsonOptions) ?? Array.Empty<MenuAddOn>(),
                new ValueComparer<IReadOnlyList<MenuAddOn>>(
                    (a, b) => (a ?? Array.Empty<MenuAddOn>()).SequenceEqual(b ?? Array.Empty<MenuAddOn>()),
                    v => v.Aggregate(0, (h, x) => HashCode.Combine(h, x.GetHashCode())),
                    v => v.ToArray()))
            .HasDefaultValueSql("'[]'::jsonb")
            .IsRequired();
        builder.Property(x => x.StartsAt);
        builder.Property(x => x.DurationMinutes);
        builder.Property(x => x.ImagePath).HasMaxLength(MenuItem.ImagePathMaxLength);
        builder.Property(x => x.ImageVersion).IsRequired();

        builder.HasIndex(x => new { x.KraftId, x.EventId });
        builder.HasIndex(x => new { x.EventId, x.Status });

        CommerceEf.ConfigureAudit(builder);
        CommerceEf.ConfigureAggregateVersion(builder);
    }
}

internal sealed class StockBucketConfiguration : IEntityTypeConfiguration<StockBucketRow>
{
    public void Configure(EntityTypeBuilder<StockBucketRow> builder)
    {
        builder.ToTable(StockBucketRow.TableName, table =>
            table.HasCheckConstraint("ck_stock_bucket_counts", "\"Held\" >= 0 AND \"Sold\" >= 0 AND (\"Total\" IS NULL OR \"Total\" >= 0)"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Kind).HasMaxLength(16).IsRequired();
        builder.HasIndex(x => new { x.Kind, x.MenuItemId }).IsUnique();
        builder.HasIndex(x => new { x.EventId, x.KraftId });
    }
}

internal sealed class StockHoldConfiguration : IEntityTypeConfiguration<StockHoldRow>
{
    public void Configure(EntityTypeBuilder<StockHoldRow> builder)
    {
        builder.ToTable(StockHoldRow.TableName);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Kind).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(16).IsRequired();
        builder.HasIndex(x => new { x.OrderId, x.Status });
    }
}
