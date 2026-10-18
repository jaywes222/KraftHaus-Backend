using System.Text.Json;
using Ticketing.Infrastructure.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Ticketing.Infrastructure.Persistence.Configurations;

public sealed class TicketingAuditRecordConfiguration : IEntityTypeConfiguration<TicketingAuditRecord>
{
    public void Configure(EntityTypeBuilder<TicketingAuditRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TicketingAuditRecord.TableName);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.OccurredAt).IsRequired();
        builder.Property(x => x.Module).HasMaxLength(TicketingAuditRecord.ModuleMaxLength).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(TicketingAuditRecord.ActionMaxLength).IsRequired();
        builder.Property(x => x.Outcome).HasMaxLength(TicketingAuditRecord.EnumMaxLength).IsRequired();
        builder.Property(x => x.ActorType).HasMaxLength(TicketingAuditRecord.EnumMaxLength).IsRequired();
        builder.Property(x => x.ActorId).HasMaxLength(TicketingAuditRecord.ActorIdMaxLength);
        builder.Property(x => x.ActorSessionId).HasMaxLength(TicketingAuditRecord.ActorIdMaxLength);
        builder.Property(x => x.TenantId);
        builder.Property(x => x.TargetType).HasMaxLength(TicketingAuditRecord.TargetTypeMaxLength);
        builder.Property(x => x.TargetId).HasMaxLength(TicketingAuditRecord.TargetIdMaxLength);
        builder.Property(x => x.TraceId).HasMaxLength(TicketingAuditRecord.TraceIdMaxLength);
        builder.Property(x => x.RequestId).HasMaxLength(TicketingAuditRecord.RequestIdMaxLength);

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
