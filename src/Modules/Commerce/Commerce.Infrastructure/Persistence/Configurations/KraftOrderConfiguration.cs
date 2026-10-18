using Commerce.Domain.Common;
using Commerce.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Commerce.Infrastructure.Persistence.Configurations;

public sealed class KraftOrderConfiguration : IEntityTypeConfiguration<KraftOrder>
{
    public void Configure(EntityTypeBuilder<KraftOrder> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("kraft_order");
        builder.Ignore(x => x.Events);
        builder.Ignore(x => x.NeedsAction);
        builder.Ignore(x => x.IsPaid);

        CommerceEf.ConfigureKey<KraftOrder, KraftOrderId>(builder);

        builder.Property(x => x.CheckoutGroupId).IsRequired();
        builder.Property(x => x.KraftId).IsRequired();
        builder.Property(x => x.KraftName).HasMaxLength(80).IsRequired();
        builder.Property(x => x.EventId).IsRequired();
        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.Reference).HasMaxLength(KraftOrder.ReferenceLength).IsRequired();
        builder.Property(x => x.PickupCode).HasMaxLength(KraftOrder.PickupCodeLength).IsRequired();
        builder.Property(x => x.CustomerUserId).IsRequired();
        builder.Property(x => x.CustomerPhone).HasMaxLength(16).IsRequired();
        builder.Property(x => x.CustomerName).HasMaxLength(KraftOrder.NameMaxLength).IsRequired();
        builder.Property(x => x.PayerPhone).HasMaxLength(16).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasMaxLength(KraftOrder.IdempotencyKeyMaxLength).IsRequired();
        builder.Property(x => x.Total).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.Subtotal).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.ServiceFee).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.ServiceFeeWaived).IsRequired();
        builder.Property(x => x.PaidIntoPlatformTill).IsRequired();
        builder.Property(x => x.CommissionPercent).HasColumnType("numeric(5,2)").IsRequired();
        builder.Property(x => x.PlatformFee).HasColumnType("numeric(18,2)");
        builder.Property(x => x.KraftRevenue).HasColumnType("numeric(18,2)");
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.PlacedAt).IsRequired();
        builder.Property(x => x.HoldExpiresAt).IsRequired();
        builder.Property(x => x.LastPaymentFailure).HasMaxLength(KraftOrder.NoteMaxLength);
        builder.Property(x => x.MpesaReceipt).HasMaxLength(32);
        builder.Property(x => x.AttentionReason).HasMaxLength(KraftOrder.NoteMaxLength);
        builder.Property(x => x.AmountToRefund).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.AttentionResolution).HasMaxLength(KraftOrder.NoteMaxLength);

        builder.HasIndex(x => x.Reference).IsUnique();
        builder.HasIndex(x => new { x.KraftId, x.PickupCode }).IsUnique();
        builder.HasIndex(x => new { x.CustomerUserId, x.IdempotencyKey, x.KraftId }).IsUnique();
        builder.HasIndex(x => x.CheckoutGroupId);
        builder.HasIndex(x => new { x.CustomerUserId, x.PlacedAt });
        builder.HasIndex(x => new { x.Status, x.HoldExpiresAt });
        builder.HasIndex(x => new { x.KraftId, x.EventId, x.Status });
        builder.HasIndex(x => new { x.EventId, x.PlacedAt });

        builder.HasMany(x => x.Lines).WithOne().HasForeignKey("OrderId").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();

        CommerceEf.ConfigureAudit(builder);
        CommerceEf.ConfigureAggregateVersion(builder);
    }
}

public sealed class KraftOrderLineConfiguration : IEntityTypeConfiguration<KraftOrderLine>
{
    public void Configure(EntityTypeBuilder<KraftOrderLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("kraft_order_line");
        CommerceEf.ConfigureKey<KraftOrderLine, KraftOrderLineId>(builder);

        builder.Ignore(x => x.LineTotal);
        builder.Property(x => x.MenuItemId).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Quantity).IsRequired();
        builder.Property(x => x.UnitPrice).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.AddOns).HasMaxLength(KraftOrderLine.AddOnsMaxLength);
        builder.Property(x => x.AgeRestricted).IsRequired();
        builder.Property(x => x.Unavailable).IsRequired();
        builder.Property(x => x.UnavailableNote).HasMaxLength(KraftOrder.NoteMaxLength);
        builder.HasIndex(x => x.MenuItemId);

        CommerceEf.ConfigureAudit(builder);
    }
}
