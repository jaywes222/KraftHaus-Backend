using Common.IntegrationEvents.Payments;
using Payments.Domain.Payments;
using Xunit;

namespace Payments.Tests;

public sealed class ReviewAndSettlementTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);

    private static Payment Mismatched()
    {
        var payment = PaymentTests.NewPayment(2000m);
        payment.MarkPromptSent("tx-1", null, Now);
        payment.ApplyProviderRecord(new ProviderRecord("tx-1", ProviderTransactionStatus.Success, 2001m, null, "QK1", "0", "ok"), Now);
        return payment;
    }

    [Fact]
    public void AcceptingAMismatchSucceeds()
    {
        var payment = Mismatched();

        var result = payment.Review(PaymentReviewDecision.AcceptAsPaid, " kept the extra shilling ", Guid.NewGuid(), Now);

        Assert.Equal(PaymentTransition.Succeeded, result.Value);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal("kept the extra shilling", payment.ReviewNote);
    }

    [Fact]
    public void RefundingAMismatchFailsThePaymentForTheOrder()
    {
        var payment = Mismatched();

        Assert.Equal(PaymentTransition.Failed, payment.Review(PaymentReviewDecision.Refunded, "reversed", Guid.NewGuid(), Now).Value);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(PaymentFailureReason.Refunded, payment.FailureReason);
    }

    [Fact]
    public void ReviewNeedsANoteAndAPaymentUnderReview()
    {
        Assert.Equal("Payments.NoteInvalid", Mismatched().Review(PaymentReviewDecision.AcceptAsPaid, "  ", Guid.NewGuid(), Now).Error!.Key);
        Assert.Equal("Payments.NotUnderReview", PaymentTests.NewPayment().Review(PaymentReviewDecision.AcceptAsPaid, "x", Guid.NewGuid(), Now).Error!.Key);
    }

    [Fact]
    public void SettlementOnlyForSuccessfulPaymentsAndNeedsAReference()
    {
        var pending = PaymentTests.NewPayment();
        Assert.Equal("Payments.NotSettleable", SettlementRecord.Record(pending, SettlementStatus.Settled, "R1", null, null, Guid.NewGuid(), Now).Error!.Key);

        var paid = Mismatched();
        paid.Review(PaymentReviewDecision.AcceptAsPaid, "ok", Guid.NewGuid(), Now);

        Assert.Equal("Payments.ReferenceInvalid", SettlementRecord.Record(paid, SettlementStatus.Settled, " ", null, null, Guid.NewGuid(), Now).Error!.Key);
        Assert.Equal("Payments.SettledAtInvalid", SettlementRecord.Record(paid, SettlementStatus.Settled, "R1", Now.AddDays(1), null, Guid.NewGuid(), Now).Error!.Key);

        var record = SettlementRecord.Record(paid, SettlementStatus.Settled, "R1", null, null, Guid.NewGuid(), Now).Value!;
        Assert.Equal(Now, record.SettledAt);

        Assert.False(record.Update(paid, SettlementStatus.Unsettled, null, null, "reopened", Guid.NewGuid(), Now).IsFailure);
        Assert.Null(record.SettledAt);
    }
}
