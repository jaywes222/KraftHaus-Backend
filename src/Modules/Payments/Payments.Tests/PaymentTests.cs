using Common.IntegrationEvents.Payments;
using Payments.Domain.Common;
using Payments.Domain.Payments;
using Xunit;

namespace Payments.Tests;

public sealed class PaymentTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);

    internal static Payment NewPayment(decimal amount = 2000m) =>
        Payment.Create(
            new PaymentId(Guid.CreateVersion7()),
            PaymentPurpose.TicketOrder,
            Guid.CreateVersion7(),
            amount,
            "+254712345678",
            "7K3M9Q2B",
            "Order 7K3M9Q",
            "token",
            Now).Value!;

    private static ProviderRecord Record(ProviderTransactionStatus status, decimal amount = 2000m) =>
        new("tx-1", status, amount, "7K3M9Q2B", status == ProviderTransactionStatus.Success ? "QK12ABC" : null, "0", "ok");

    [Theory]
    [InlineData(0)]
    [InlineData(10.5)]
    [InlineData(250001)]
    public void AmountsMustBeWholeShillingsInRange(decimal amount)
    {
        var result = Payment.Create(new PaymentId(Guid.CreateVersion7()), PaymentPurpose.TicketOrder, Guid.CreateVersion7(),
            amount, "+254712345678", "REF", "Desc", "token", Now);

        Assert.Equal("Payments.AmountInvalid", result.Error!.Key);
    }

    [Fact]
    public void ReferenceAndDescriptionFitMpesaLimits()
    {
        Assert.True(Payment.Create(new PaymentId(Guid.CreateVersion7()), PaymentPurpose.TicketOrder, Guid.CreateVersion7(),
            100m, "+254712345678", "1234567890123", "Desc", "token", Now).IsFailure);
        Assert.True(Payment.Create(new PaymentId(Guid.CreateVersion7()), PaymentPurpose.TicketOrder, Guid.CreateVersion7(),
            100m, "+254712345678", "REF", "12345678901234", "token", Now).IsFailure);
    }

    [Fact]
    public void SuccessConfirmsOnce()
    {
        var payment = NewPayment();
        payment.MarkPromptSent("tx-1", null, Now);

        Assert.Equal(PaymentTransition.None, payment.ApplyProviderRecord(Record(ProviderTransactionStatus.Processing), Now));
        Assert.Equal(PaymentTransition.Succeeded, payment.ApplyProviderRecord(Record(ProviderTransactionStatus.Success), Now));
        Assert.Equal(PaymentTransition.None, payment.ApplyProviderRecord(Record(ProviderTransactionStatus.Success), Now));
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal("QK12ABC", payment.MpesaReceipt);
        Assert.Equal(2, payment.CheckCount - 1);
    }

    [Fact]
    public void WrongAmountGoesToReviewNotSuccess()
    {
        var payment = NewPayment();
        payment.MarkPromptSent("tx-1", null, Now);

        Assert.Equal(PaymentTransition.FlaggedForReview, payment.ApplyProviderRecord(Record(ProviderTransactionStatus.Success, 20m), Now));
        Assert.Equal(PaymentStatus.NeedsReview, payment.Status);
        Assert.Equal(20m, payment.ReportedAmount);
    }

    [Fact]
    public void ExpiredPaymentCanStillSucceedLateButIsNotReportedFailedTwice()
    {
        var late = NewPayment();
        late.MarkPromptSent("tx-1", null, Now);
        Assert.Equal(PaymentTransition.Failed, late.Expire(Now));
        Assert.Equal(PaymentTransition.SucceededLate, late.ApplyProviderRecord(Record(ProviderTransactionStatus.Success), Now));

        var failed = NewPayment();
        failed.MarkPromptSent("tx-1", null, Now);
        failed.Expire(Now);
        Assert.Equal(PaymentTransition.None, failed.ApplyProviderRecord(Record(ProviderTransactionStatus.Failed), Now));
        Assert.Equal(PaymentStatus.Failed, failed.Status);
    }

    [Fact]
    public void CancelledIsFinal()
    {
        var payment = NewPayment();
        payment.MarkPromptSent("tx-1", null, Now);

        Assert.Equal(PaymentTransition.Failed, payment.ApplyProviderRecord(Record(ProviderTransactionStatus.Cancelled), Now));
        Assert.Equal(PaymentFailureReason.Cancelled, payment.FailureReason);
        Assert.Equal(PaymentTransition.None, payment.ApplyProviderRecord(Record(ProviderTransactionStatus.Success), Now));
    }

    [Fact]
    public void NotSentOnlyFromInitiatingAndCallbackCanAdoptTheTransaction()
    {
        var refused = NewPayment();
        refused.MarkNotSent("RATE_LIMIT_EXCEEDED", "slow down", Now);
        Assert.Equal(PaymentStatus.NotSent, refused.Status);

        var adopted = NewPayment();
        adopted.AdoptProviderTransaction("tx-9", Now);
        Assert.Equal(PaymentStatus.Pending, adopted.Status);
        Assert.Equal("tx-9", adopted.ProviderTransactionId);
    }

    [Fact]
    public void OnlyTheTokenHashIsStored()
    {
        var payment = NewPayment();

        Assert.Equal(Payment.HashToken("token"), payment.CallbackTokenHash);
        Assert.Equal(64, payment.CallbackTokenHash.Length);
    }
}
