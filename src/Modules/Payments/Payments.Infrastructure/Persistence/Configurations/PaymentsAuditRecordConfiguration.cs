using System.Text.Json;
using Payments.Infrastructure.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Payments.Infrastructure.Persistence.Configurations;

public sealed class PaymentsAuditRecordConfiguration : IEntityTypeConfiguration<PaymentsAuditRecord>
{
    public void Configure(EntityTypeBuilder<PaymentsAuditRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(PaymentsAuditRecord.TableName);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.OccurredAt).IsRequired();
        builder.Property(x => x.Module).HasMaxLength(PaymentsAuditRecord.ModuleMaxLength).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(PaymentsAuditRecord.ActionMaxLength).IsRequired();
        builder.Property(x => x.Outcome).HasMaxLength(PaymentsAuditRecord.EnumMaxLength).IsRequired();
        builder.Property(x => x.ActorType).HasMaxLength(PaymentsAuditRecord.EnumMaxLength).IsRequired();
        builder.Property(x => x.ActorId).HasMaxLength(PaymentsAuditRecord.ActorIdMaxLength);
        builder.Property(x => x.ActorSessionId).HasMaxLength(PaymentsAuditRecord.ActorIdMaxLength);
        builder.Property(x => x.TenantId);
        builder.Property(x => x.TargetType).HasMaxLength(PaymentsAuditRecord.TargetTypeMaxLength);
        builder.Property(x => x.TargetId).HasMaxLength(PaymentsAuditRecord.TargetIdMaxLength);
        builder.Property(x => x.TraceId).HasMaxLength(PaymentsAuditRecord.TraceIdMaxLength);
        builder.Property(x => x.RequestId).HasMaxLength(PaymentsAuditRecord.RequestIdMaxLength);

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
