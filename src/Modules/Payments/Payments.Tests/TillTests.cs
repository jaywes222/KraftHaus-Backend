using Payments.Domain.Common;
using Payments.Domain.Destinations;
using Xunit;

namespace Payments.Tests;

public sealed class TillTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void SubmittingChecksTheShortcodeAndAccountNumber()
    {
        var result = PaymentDestination.SubmitKraftTill(NewId(), Guid.CreateVersion7(), Guid.CreateVersion7(), "Mama Pima till",
            ShortcodeType.BuyGoods, "12 34", "ACC1", Guid.CreateVersion7(), Now);

        Assert.Equal("Payments.TillInvalid", result.Error!.Key);
        Assert.Contains(result.Error.SubErrors!, x => x.StartsWith("Shortcode:", StringComparison.Ordinal));
        Assert.Contains(result.Error.SubErrors!, x => x.StartsWith("AccountNumber:", StringComparison.Ordinal));
    }

    [Fact]
    public void ApprovalNeedsAPassedTest()
    {
        var till = NewTill();

        Assert.Equal("Payments.TillTestRequired", till.Approve(Guid.CreateVersion7(), Now).Error!.Key);

        var payment = Guid.CreateVersion7();
        Assert.False(till.StartTest(Guid.CreateVersion7(), payment, Now).IsFailure);
        Assert.Equal("Payments.TillTestInProgress", till.StartTest(Guid.CreateVersion7(), Guid.CreateVersion7(), Now).Error!.Key);
        Assert.Equal("Payments.TillTestRequired", till.Approve(Guid.CreateVersion7(), Now).Error!.Key);

        Assert.True(till.RecordTestResult(payment, passed: true, Now));
        Assert.False(till.Approve(Guid.CreateVersion7(), Now).IsFailure);
        Assert.Equal(DestinationStatus.Approved, till.Status);
    }

    [Fact]
    public void AnOldTestResultIsIgnored()
    {
        var till = NewTill();
        var first = Guid.CreateVersion7();
        till.StartTest(Guid.CreateVersion7(), first, Now);
        till.RecordTestResult(first, passed: false, Now);

        var second = Guid.CreateVersion7();
        till.StartTest(Guid.CreateVersion7(), second, Now);

        Assert.False(till.RecordTestResult(first, passed: true, Now));
        Assert.Equal(TillTestStatus.Pending, till.TestStatus);
    }

    [Fact]
    public void RejectingAndDisablingNeedAReasonAndTheRightState()
    {
        var till = NewTill();

        Assert.Equal("Payments.TillDecisionInvalid", till.Reject(" ", Guid.CreateVersion7(), Now).Error!.Key);
        Assert.Equal("Payments.TillNotApproved", till.Disable("closed", Guid.CreateVersion7(), Now).Error!.Key);
        Assert.False(till.Reject("Wrong till number", Guid.CreateVersion7(), Now).IsFailure);
        Assert.False(till.IsActive);
        Assert.Equal("Payments.TillNotTestable", till.StartTest(Guid.CreateVersion7(), Guid.CreateVersion7(), Now).Error!.Key);
    }

    private static PaymentDestination NewTill() =>
        PaymentDestination.SubmitKraftTill(NewId(), Guid.CreateVersion7(), Guid.CreateVersion7(), "Mama Pima till",
            ShortcodeType.Paybill, "522522", "MAMAPIMA", Guid.CreateVersion7(), Now).Value!;

    private static PaymentDestinationId NewId() => new(Guid.CreateVersion7());
}
