using Commerce.Domain.Common;
using Commerce.Domain.Settlements;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Commerce.Infrastructure.Persistence.Configurations;

public sealed class KraftSettlementConfiguration : IEntityTypeConfiguration<KraftSettlement>
{
    public void Configure(EntityTypeBuilder<KraftSettlement> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("kraft_settlement");
        builder.Ignore(x => x.Events);

        CommerceEf.ConfigureKey<KraftSettlement, KraftSettlementId>(builder);

        builder.Property(x => x.KraftId).IsRequired();
        builder.Property(x => x.EventId).IsRequired();
        builder.Property(x => x.Direction).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(x => x.Amount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.Reference).HasMaxLength(KraftSettlement.ReferenceMaxLength).IsRequired();
        builder.Property(x => x.Note).HasMaxLength(KraftSettlement.NoteMaxLength);
        builder.Property(x => x.PaidAt).IsRequired();
        builder.Property(x => x.RecordedBy).IsRequired();

        builder.HasIndex(x => new { x.EventId, x.KraftId });

        CommerceEf.ConfigureAudit(builder);
        CommerceEf.ConfigureAggregateVersion(builder);
    }
}
