using Common.Domain.StronglyTypedIds;
using Common.Infrastructure.Persistence.ValueConverters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Payments.Domain.Common;
using Payments.Domain.Destinations;
using Payments.Domain.Payments;

namespace Payments.Infrastructure.Persistence.Configurations;

public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    private static readonly ValueConverter<ApplicationUserId?, Guid?> UserIdConverter = new(
        id => id.HasValue ? id.Value.Value : null,
        value => value.HasValue ? new ApplicationUserId(value.Value) : null);

    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("payment");
        builder.Ignore(x => x.Events);
        builder.Ignore(x => x.IsFinal);
        builder.Ignore(x => x.FailureReason);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasConversion(new StronglyTypedIdValueConverter<PaymentId>())
            .ValueGeneratedNever();

        builder.Property(x => x.Purpose).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.ReferenceId).IsRequired();
        builder.Property(x => x.Amount).HasColumnType("numeric(18,2)").IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Phone).HasMaxLength(16).IsRequired();
        builder.Property(x => x.AccountReference).HasMaxLength(Payment.AccountReferenceMaxLength).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(Payment.DescriptionMaxLength).IsRequired();
        builder.Property(x => x.Provider).HasMaxLength(32).IsRequired();
        builder.Property(x => x.CallbackTokenHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.ProviderTransactionId).HasMaxLength(Payment.ProviderIdMaxLength);
        builder.Property(x => x.ProviderCheckoutId).HasMaxLength(Payment.ProviderIdMaxLength);
        builder.Property(x => x.MpesaReceipt).HasMaxLength(Payment.ProviderIdMaxLength);
        builder.Property(x => x.ResultCode).HasMaxLength(Payment.ProviderIdMaxLength);
        builder.Property(x => x.ResultDescription).HasMaxLength(Payment.ResultDescriptionMaxLength);
        builder.Property(x => x.ProviderErrorCode).HasMaxLength(Payment.ProviderIdMaxLength);
        builder.Property(x => x.ReportedAmount).HasColumnType("numeric(18,2)");
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.ReviewDecision).HasConversion<string>().HasMaxLength(16);
        builder.Property(x => x.ReviewNote).HasMaxLength(Payment.ResultDescriptionMaxLength);

        builder.Property(x => x.DestinationId);
        builder.Property(x => x.ProviderChannelId);
        builder.HasIndex(x => x.DestinationId).HasFilter("\"DestinationId\" IS NOT NULL");

        builder.HasIndex(x => x.CallbackTokenHash).IsUnique();
        builder.HasIndex(x => x.ProviderTransactionId).IsUnique().HasFilter("\"ProviderTransactionId\" IS NOT NULL");
        builder.HasIndex(x => new { x.Purpose, x.ReferenceId });
        builder.HasIndex(x => new { x.Status, x.CreatedAt });

        builder.Property(x => x.Version).HasColumnName("aggregate_version").IsConcurrencyToken();
        builder.Property(x => x.CreatedOn).IsRequired();
        builder.Property(x => x.CreatedBy).HasConversion(UserIdConverter);
        builder.Property(x => x.LastModifiedOn);
        builder.Property(x => x.LastModifiedBy).HasConversion(UserIdConverter);
    }
}

public sealed class PaymentDestinationConfiguration : IEntityTypeConfiguration<PaymentDestination>
{
    /// <summary>At most one submitted or approved till per Kraft and event.</summary>
    public const string ActiveTillIndex = "ux_payment_destination_active_till";

    private static readonly ValueConverter<ApplicationUserId?, Guid?> UserIdConverter = new(
        id => id.HasValue ? id.Value.Value : null,
        value => value.HasValue ? new ApplicationUserId(value.Value) : null);

    public void Configure(EntityTypeBuilder<PaymentDestination> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("payment_destination");
        builder.Ignore(x => x.Events);
        builder.Ignore(x => x.IsActive);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasConversion(new StronglyTypedIdValueConverter<PaymentDestinationId>()).ValueGeneratedNever();

        builder.Property(x => x.Kind).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.KraftId).IsRequired();
        builder.Property(x => x.EventId).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(PaymentDestination.NameMaxLength).IsRequired();
        builder.Property(x => x.ShortcodeType).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.Shortcode).HasMaxLength(PaymentDestination.ShortcodeMaxLength).IsRequired();
        builder.Property(x => x.AccountNumber).HasMaxLength(PaymentDestination.AccountNumberMaxLength);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.ProviderChannelId);
        builder.Property(x => x.TestStatus).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.TestPaymentId);
        builder.Property(x => x.TestedAt);
        builder.Property(x => x.SubmittedBy).IsRequired();
        builder.Property(x => x.SubmittedAt).IsRequired();
        builder.Property(x => x.DecidedBy);
        builder.Property(x => x.DecidedAt);
        builder.Property(x => x.DecisionNote).HasMaxLength(PaymentDestination.NoteMaxLength);

        builder.HasIndex(x => new { x.KraftId, x.EventId })
            .IsUnique()
            .HasFilter("\"Status\" IN ('Submitted', 'Approved')")
            .HasDatabaseName(ActiveTillIndex);
        builder.HasIndex(x => new { x.EventId, x.Status });
        builder.HasIndex(x => x.TestPaymentId).HasFilter("\"TestPaymentId\" IS NOT NULL");

        builder.Property(x => x.Version).HasColumnName("aggregate_version").IsConcurrencyToken();
        builder.Property(x => x.CreatedOn).IsRequired();
        builder.Property(x => x.CreatedBy).HasConversion(UserIdConverter);
        builder.Property(x => x.LastModifiedOn);
        builder.Property(x => x.LastModifiedBy).HasConversion(UserIdConverter);
    }
}

public sealed class SettlementRecordConfiguration : IEntityTypeConfiguration<SettlementRecord>
{
    private static readonly ValueConverter<ApplicationUserId?, Guid?> UserIdConverter = new(
        id => id.HasValue ? id.Value.Value : null,
        value => value.HasValue ? new ApplicationUserId(value.Value) : null);

    public void Configure(EntityTypeBuilder<SettlementRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("settlement_record");
        builder.Ignore(x => x.Events);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasColumnName("PaymentId")
            .HasConversion(new StronglyTypedIdValueConverter<PaymentId>())
            .ValueGeneratedNever();

        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(x => x.Reference).HasMaxLength(SettlementRecord.ReferenceMaxLength);
        builder.Property(x => x.Note).HasMaxLength(SettlementRecord.NoteMaxLength);
        builder.Property(x => x.RecordedBy).IsRequired();
        builder.Property(x => x.RecordedAt).IsRequired();

        builder.HasOne<Payment>().WithOne().HasForeignKey<SettlementRecord>(x => x.Id).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.Status, x.SettledAt });

        builder.Property(x => x.Version).HasColumnName("aggregate_version").IsConcurrencyToken();
        builder.Property(x => x.CreatedOn).IsRequired();
        builder.Property(x => x.CreatedBy).HasConversion(UserIdConverter);
        builder.Property(x => x.LastModifiedOn);
        builder.Property(x => x.LastModifiedBy).HasConversion(UserIdConverter);
    }
}
