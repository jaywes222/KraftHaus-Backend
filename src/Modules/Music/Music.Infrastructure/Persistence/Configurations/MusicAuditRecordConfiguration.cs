using System.Text.Json;
using Music.Infrastructure.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Music.Infrastructure.Persistence.Configurations;

public sealed class MusicAuditRecordConfiguration : IEntityTypeConfiguration<MusicAuditRecord>
{
    public void Configure(EntityTypeBuilder<MusicAuditRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(MusicAuditRecord.TableName);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.OccurredAt).IsRequired();
        builder.Property(x => x.Module).HasMaxLength(MusicAuditRecord.ModuleMaxLength).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(MusicAuditRecord.ActionMaxLength).IsRequired();
        builder.Property(x => x.Outcome).HasMaxLength(MusicAuditRecord.EnumMaxLength).IsRequired();
        builder.Property(x => x.ActorType).HasMaxLength(MusicAuditRecord.EnumMaxLength).IsRequired();
        builder.Property(x => x.ActorId).HasMaxLength(MusicAuditRecord.ActorIdMaxLength);
        builder.Property(x => x.ActorSessionId).HasMaxLength(MusicAuditRecord.ActorIdMaxLength);
        builder.Property(x => x.TenantId);
        builder.Property(x => x.TargetType).HasMaxLength(MusicAuditRecord.TargetTypeMaxLength);
        builder.Property(x => x.TargetId).HasMaxLength(MusicAuditRecord.TargetIdMaxLength);
        builder.Property(x => x.TraceId).HasMaxLength(MusicAuditRecord.TraceIdMaxLength);
        builder.Property(x => x.RequestId).HasMaxLength(MusicAuditRecord.RequestIdMaxLength);

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
