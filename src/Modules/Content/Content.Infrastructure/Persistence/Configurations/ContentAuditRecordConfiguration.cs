using System.Text.Json;
using Content.Infrastructure.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Content.Infrastructure.Persistence.Configurations;

public sealed class ContentAuditRecordConfiguration : IEntityTypeConfiguration<ContentAuditRecord>
{
    public void Configure(EntityTypeBuilder<ContentAuditRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(ContentAuditRecord.TableName);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.OccurredAt).IsRequired();
        builder.Property(x => x.Module).HasMaxLength(ContentAuditRecord.ModuleMaxLength).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(ContentAuditRecord.ActionMaxLength).IsRequired();
        builder.Property(x => x.Outcome).HasMaxLength(ContentAuditRecord.EnumMaxLength).IsRequired();
        builder.Property(x => x.ActorType).HasMaxLength(ContentAuditRecord.EnumMaxLength).IsRequired();
        builder.Property(x => x.ActorId).HasMaxLength(ContentAuditRecord.ActorIdMaxLength);
        builder.Property(x => x.ActorSessionId).HasMaxLength(ContentAuditRecord.ActorIdMaxLength);
        builder.Property(x => x.TenantId);
        builder.Property(x => x.TargetType).HasMaxLength(ContentAuditRecord.TargetTypeMaxLength);
        builder.Property(x => x.TargetId).HasMaxLength(ContentAuditRecord.TargetIdMaxLength);
        builder.Property(x => x.TraceId).HasMaxLength(ContentAuditRecord.TraceIdMaxLength);
        builder.Property(x => x.RequestId).HasMaxLength(ContentAuditRecord.RequestIdMaxLength);

        builder.Property(x => x.Metadata)
            .HasColumnType("jsonb")
            .HasConversion(
                new ValueConverter<IReadOnlyDictionary<string, string>, string>(
                    value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
                    json => Deserialize(json)),
                new ValueComparer<IReadOnlyDictionary<string, string>>(
                    (left, right) => ReferenceEquals(left, right),
                    value => value.Count,
                    value => value))
            .IsRequired();

        builder.HasIndex(x => x.OccurredAt).IsDescending();
        builder.HasIndex(x => new { x.TargetType, x.TargetId, x.OccurredAt }).IsDescending(false, false, true);
        builder.HasIndex(x => new { x.ActorId, x.OccurredAt }).IsDescending(false, true);
        builder.HasIndex(x => x.TraceId);
    }

    private static Dictionary<string, string> Deserialize(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(json)
        ?? new Dictionary<string, string>(StringComparer.Ordinal);
}
