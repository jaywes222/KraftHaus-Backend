using Ticketing.Domain.Common;
using Ticketing.Domain.Orders;
using Ticketing.Domain.Tickets;
using Xunit;
using Common.Domain.Codes;

namespace Ticketing.Tests;

public sealed class OrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);

    private static Order NewOrder(decimal price = 1000m) =>
        Order.Place(
            new OrderId(Guid.CreateVersion7()),
            Guid.CreateVersion7(),
            "7K3M9Q2B",
            new OrderCustomer(Guid.CreateVersion7(), "+254712345678", "Wanjiru"),
            "+254712345678",
            "key-1",
            [OrderLine.Create(new OrderLineId(Guid.CreateVersion7()), new OrderLineSpec(OrderItemKind.Admission, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, "Regular", 2, 1, price))],
            10,
            Now).Value!;

    [Fact]
    public void HoldAndPaymentAttempts()
    {
        var order = NewOrder();

        Assert.Equal(Now.AddMinutes(10), order.HoldExpiresAt);
        Assert.False(order.StartPayment(Guid.NewGuid(), 2, Now).IsFailure);
        Assert.False(order.StartPayment(Guid.NewGuid(), 2, Now).IsFailure);
        Assert.Equal("Ticketing.TooManyPaymentAttempts", order.StartPayment(Guid.NewGuid(), 2, Now).Error!.Key);
        Assert.Equal("Ticketing.HoldExpired", NewOrder().StartPayment(Guid.NewGuid(), 5, Now.AddMinutes(10)).Error!.Key);
    }

    [Fact]
    public void OnlyTheCurrentPaymentsFailureCounts()
    {
        var order = NewOrder();
        var first = Guid.NewGuid();
        order.StartPayment(first, 5, Now);
        var second = Guid.NewGuid();
        order.StartPayment(second, 5, Now);

        order.RecordPaymentFailed(first, "Cancelled");
        Assert.Null(order.LastPaymentFailure);

        order.RecordPaymentFailed(second, "Cancelled");
        Assert.Equal("Cancelled", order.LastPaymentFailure);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(999.5)]
    public void FreeOrFractionalTotalsCannotBePaidByMpesa(decimal price)
    {
        var result = Order.Place(new OrderId(Guid.CreateVersion7()), Guid.CreateVersion7(), "REF00001",
            new OrderCustomer(Guid.CreateVersion7(), "+254712345678", "W"), "+254712345678", "k",
            [OrderLine.Create(new OrderLineId(Guid.CreateVersion7()), new OrderLineSpec(OrderItemKind.Admission, Guid.NewGuid(), Guid.NewGuid(), null, null, "R", 1, 1, price))],
            10, Now);

        Assert.Equal("Ticketing.PriceNotPayable", result.Error!.Key);
    }

    [Fact]
    public void ExpiredOrdersCanStillBePaidLateSecondPaymentsAreFlagged()
    {
        var order = NewOrder();
        Assert.True(order.Expire(Now));
        Assert.False(order.MarkPaid(Guid.NewGuid(), Now).IsFailure);
        Assert.True(order.Events.OfType<OrderPaid>().Single().Late);

        order.FlagUnusablePayment(Guid.NewGuid(), "second payment");
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal("second payment", order.AttentionReason);
    }

    [Fact]
    public void TicketsCheckInOnceOnTheirDateAndTransfersAreLimited()
    {
        var date = Guid.NewGuid();
        var ticket = Ticket.Issue(new TicketId(Guid.CreateVersion7()), new TicketIssue(
            new OrderId(Guid.NewGuid()), new OrderLineId(Guid.NewGuid()), Guid.NewGuid(), date, Guid.NewGuid(), null,
            "+254712345678", "Wanjiru", "KraftHaus 001", "Regular", null, Now, null, null), TicketStatus.Valid, Now);

        var code = ticket.Code;
        Assert.Equal(TransferOutcome.SameHolder, ticket.Transfer("+254712345678", "Me", 1));
        Assert.Equal(TransferOutcome.Transferred, ticket.Transfer("+254722000111", "Achieng", 1));
        Assert.NotEqual(code, ticket.Code);
        Assert.Equal(TransferOutcome.LimitReached, ticket.Transfer("+254733000222", "Otieno", 1));

        Assert.Equal(CheckInOutcome.WrongDate, ticket.CheckIn(Guid.NewGuid(), Guid.NewGuid(), CheckInMethod.Qr, Now));
        Assert.Equal(CheckInOutcome.Admitted, ticket.CheckIn(date, Guid.NewGuid(), CheckInMethod.Qr, Now));
        Assert.Equal(CheckInOutcome.AlreadyCheckedIn, ticket.CheckIn(date, Guid.NewGuid(), CheckInMethod.Manual, Now));
        Assert.Equal(TransferOutcome.NotTransferable, ticket.Transfer("+254744000333", "Late", 5));
    }

    [Fact]
    public void ShortCodesAvoidConfusableLetters()
    {
        var code = ShortCode.New(200);

        Assert.DoesNotContain('I', code);
        Assert.DoesNotContain('L', code);
        Assert.DoesNotContain('O', code);
        Assert.DoesNotContain('U', code);
        Assert.Equal("0K1M1", ShortCode.Normalize("ok-im l"));
    }
}

public sealed class InstallmentOrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);

    private static Order NewOrder(decimal unitPrice, int units) =>
        Order.Place(
            new OrderId(Guid.CreateVersion7()),
            Guid.CreateVersion7(),
            "7K3M9Q2B",
            new OrderCustomer(Guid.CreateVersion7(), "+254712345678", "Wanjiru"),
            "+254712345678",
            "key-1",
            [OrderLine.Create(new OrderLineId(Guid.CreateVersion7()), new OrderLineSpec(OrderItemKind.Admission, Guid.NewGuid(), Guid.NewGuid(), null, null, "Regular", units, 1, unitPrice))],
            10,
            Now,
            new LipaMdogoMdogoTerms(50, Now.AddDays(13))).Value!;

    [Theory]
    [InlineData(1001, 3, 1502, 1501)]
    [InlineData(1000, 2, 1000, 1000)]
    [InlineData(3, 1, 2, 1)]
    public void DepositRoundsUpToWholeShillings(decimal price, int units, decimal deposit, decimal balance)
    {
        var order = NewOrder(price, units);

        Assert.Equal(PaymentPlan.LipaMdogoMdogo, order.PaymentPlan);
        Assert.Equal(deposit, order.AmountDueNow);
        Assert.Equal(price * units - deposit, balance);
    }

    [Fact]
    public void DepositThenBalanceThenPaid()
    {
        var order = NewOrder(1000, 2);
        var deposit = Guid.NewGuid();
        order.StartPayment(deposit, 5, Now);

        Assert.False(order.MarkPaid(deposit, Now).IsFailure);
        Assert.Equal(OrderStatus.PartiallyPaid, order.Status);
        Assert.Equal(1000m, order.AmountDueNow);
        Assert.Equal(0, order.PaymentAttempts);

        var balance = Guid.NewGuid();
        Assert.False(order.StartPayment(balance, 5, Now.AddDays(5)).IsFailure);
        Assert.Equal(1000m, order.Events.OfType<OrderPaymentStarted>().Last().Amount);

        Assert.False(order.MarkBalancePaid(balance, Now.AddDays(5)).IsFailure);
        Assert.Equal(OrderStatus.Paid, order.Status);
        Assert.Equal(0m, order.BalanceDue);
    }

    [Fact]
    public void UnpaidBalanceCancelsAndLateBalanceIsRefused()
    {
        var order = NewOrder(1000, 2);
        order.StartPayment(Guid.NewGuid(), 5, Now);
        order.MarkPaid(order.CurrentPaymentId!.Value, Now);

        Assert.True(order.CancelForUnpaidBalance(Now.AddDays(15)));
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.True(order.MarkBalancePaid(Guid.NewGuid(), Now.AddDays(16)).IsFailure);
        Assert.False(order.CancelForUnpaidBalance(Now.AddDays(16)));
    }

    [Fact]
    public void TooSmallForInstallments()
    {
        var result = Order.Place(
            new OrderId(Guid.CreateVersion7()), Guid.CreateVersion7(), "REF00001",
            new OrderCustomer(Guid.CreateVersion7(), "+254712345678", "W"), "+254712345678", "k",
            [OrderLine.Create(new OrderLineId(Guid.CreateVersion7()), new OrderLineSpec(OrderItemKind.Admission, Guid.NewGuid(), Guid.NewGuid(), null, null, "R", 1, 1, 1m))],
            10, Now, new LipaMdogoMdogoTerms(50, Now.AddDays(13)));

        Assert.Equal("Ticketing.LipaMdogoMdogoUnavailable", result.Error!.Key);
    }
}
