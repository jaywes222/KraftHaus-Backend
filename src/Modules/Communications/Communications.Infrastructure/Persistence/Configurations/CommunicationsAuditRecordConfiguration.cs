using System.Text.Json;
using Communications.Infrastructure.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Communications.Infrastructure.Persistence.Configurations;

public sealed class CommunicationsAuditRecordConfiguration : IEntityTypeConfiguration<CommunicationsAuditRecord>
{
    public void Configure(EntityTypeBuilder<CommunicationsAuditRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(CommunicationsAuditRecord.TableName);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.OccurredAt).IsRequired();
        builder.Property(x => x.Module).HasMaxLength(CommunicationsAuditRecord.ModuleMaxLength).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(CommunicationsAuditRecord.ActionMaxLength).IsRequired();
        builder.Property(x => x.Outcome).HasMaxLength(CommunicationsAuditRecord.EnumMaxLength).IsRequired();
        builder.Property(x => x.ActorType).HasMaxLength(CommunicationsAuditRecord.EnumMaxLength).IsRequired();
        builder.Property(x => x.ActorId).HasMaxLength(CommunicationsAuditRecord.ActorIdMaxLength);
        builder.Property(x => x.ActorSessionId).HasMaxLength(CommunicationsAuditRecord.ActorIdMaxLength);
        builder.Property(x => x.TenantId);
        builder.Property(x => x.TargetType).HasMaxLength(CommunicationsAuditRecord.TargetTypeMaxLength);
        builder.Property(x => x.TargetId).HasMaxLength(CommunicationsAuditRecord.TargetIdMaxLength);
        builder.Property(x => x.TraceId).HasMaxLength(CommunicationsAuditRecord.TraceIdMaxLength);
        builder.Property(x => x.RequestId).HasMaxLength(CommunicationsAuditRecord.RequestIdMaxLength);

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
