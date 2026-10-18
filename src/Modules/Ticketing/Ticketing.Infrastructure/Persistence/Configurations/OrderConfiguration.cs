using Common.Infrastructure.Persistence.ValueConverters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ticketing.Domain.Common;
using Ticketing.Domain.Orders;
using Ticketing.Domain.Tickets;
using Ticketing.Infrastructure.Checkout;

namespace Ticketing.Infrastructure.Persistence.Configurations;

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("order");
        builder.Ignore(x => x.Events);

        TicketingEf.ConfigureKey<Order, OrderId>(builder);

        builder.Property(x => x.EventId).IsRequired();
        builder.Property(x => x.Reference).HasMaxLength(Order.ReferenceLength).IsRequired();
        builder.Property(x => x.CustomerUserId).IsRequired();
        builder.Property(x => x.CustomerPhone).HasMaxLength(16).IsRequired();
        builder.Property(x => x.CustomerName).HasMaxLength(Order.NameMaxLength).IsRequired();
        builder.Property(x => x.PayerPhone).HasMaxLength(16).IsRequired();
        builder.Property(x => x.IdempotencyKey).HasMaxLength(Order.IdempotencyKeyMaxLength).IsRequired();
        builder.Property(x => x.Total).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.Subtotal).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.PlatformFee).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.FeeWaived).IsRequired();
        builder.Property(x => x.Admissions).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(x => x.PlacedAt).IsRequired();
        builder.Property(x => x.HoldExpiresAt).IsRequired();
        builder.Property(x => x.LastPaymentFailure).HasMaxLength(Order.FailureMaxLength);
        builder.Property(x => x.AttentionReason).HasMaxLength(Order.FailureMaxLength);
        builder.Property(x => x.PaymentPlan).HasConversion<string>().HasMaxLength(24).HasDefaultValue(PaymentPlan.Full).HasSentinel(PaymentPlan.Full).IsRequired();
        builder.Property(x => x.DepositAmount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.AmountPaid).HasColumnType("numeric(18,2)").IsRequired();
        builder.Ignore(x => x.BalanceDue);
        builder.Ignore(x => x.NeedsAction);
        builder.Property(x => x.AttentionResolution).HasMaxLength(Order.FailureMaxLength * 2);
        builder.Ignore(x => x.AmountDueNow);
        builder.HasIndex(x => new { x.Status, x.BalanceDueAt });

        builder.HasMany(x => x.Lines).WithOne().HasForeignKey("OrderId").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(x => x.Lines).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();

        builder.HasIndex(x => x.Reference).IsUnique();
        builder.HasIndex(x => new { x.CustomerUserId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.EventId, x.Status, x.PlacedAt });
        builder.HasIndex(x => new { x.Status, x.HoldExpiresAt });
        builder.HasIndex(x => new { x.CustomerUserId, x.EventId, x.Status });

        TicketingEf.ConfigureAggregateVersion(builder);
        TicketingEf.ConfigureAudit(builder);
    }
}

public sealed class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("order_line");

        TicketingEf.ConfigureKey<OrderLine, OrderLineId>(builder);

        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(OrderLine.NameMaxLength).IsRequired();
        builder.Property(x => x.UnitPrice).HasColumnType("numeric(18,2)").IsRequired();
        builder.Ignore(x => x.Admissions);
        builder.Ignore(x => x.LineTotal);

        TicketingEf.ConfigureAudit(builder);
    }
}

public sealed class TicketConfiguration : IEntityTypeConfiguration<Ticket>
{
    public void Configure(EntityTypeBuilder<Ticket> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ticket");
        builder.Ignore(x => x.Events);

        TicketingEf.ConfigureKey<Ticket, TicketId>(builder);

        builder.Property(x => x.OrderId).HasConversion(new StronglyTypedIdValueConverter<OrderId>()).IsRequired();
        builder.Property(x => x.OrderLineId).HasConversion(new StronglyTypedIdValueConverter<OrderLineId>()).IsRequired();
        builder.Property(x => x.Code).HasMaxLength(Ticket.CodeLength).IsRequired();
        builder.Property(x => x.HolderPhone).HasMaxLength(16).IsRequired();
        builder.Property(x => x.HolderName).HasMaxLength(Ticket.NameMaxLength).IsRequired();
        builder.Property(x => x.AttendeeName).HasMaxLength(Ticket.NameMaxLength);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.EventTitle).HasMaxLength(Ticket.NameMaxLength).IsRequired();
        builder.Property(x => x.TierName).HasMaxLength(Ticket.NameMaxLength).IsRequired();
        builder.Property(x => x.DateLabel).HasMaxLength(Ticket.NameMaxLength);
        builder.Property(x => x.VenueName).HasMaxLength(Ticket.NameMaxLength);
        builder.Property(x => x.CheckInMethod).HasConversion<string>().HasMaxLength(8);

        builder.HasIndex(x => x.Code).IsUnique();
        builder.HasIndex(x => x.HolderPhone);
        builder.HasIndex(x => x.OrderId);
        builder.HasIndex(x => new { x.EventDateId, x.Status });

        TicketingEf.ConfigureAggregateVersion(builder);
        TicketingEf.ConfigureAudit(builder);
    }
}

internal sealed class CapacityBucketConfiguration : IEntityTypeConfiguration<CapacityBucketRow>
{
    public void Configure(EntityTypeBuilder<CapacityBucketRow> builder)
    {
        builder.ToTable(CapacityBucketRow.TableName, table =>
        {
            table.HasCheckConstraint("ck_capacity_bucket_counts", "\"Held\" >= 0 AND \"Sold\" >= 0 AND \"Total\" >= 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Kind).HasMaxLength(16).IsRequired();
        builder.HasIndex(x => new { x.Kind, x.RefId }).IsUnique();
        builder.HasIndex(x => x.EventId);
    }
}

internal sealed class CapacityHoldConfiguration : IEntityTypeConfiguration<CapacityHoldRow>
{
    public void Configure(EntityTypeBuilder<CapacityHoldRow> builder)
    {
        builder.ToTable(CapacityHoldRow.TableName);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Kind).HasMaxLength(16).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(16).IsRequired();
        builder.HasIndex(x => new { x.OrderId, x.Status });
    }
}
