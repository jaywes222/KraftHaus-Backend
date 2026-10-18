using Ticketing.Domain.Common;
using Common.Application.Fees;
using Ticketing.Application.Orders;
using Ticketing.Domain.Orders;
using Xunit;

namespace Ticketing.Tests;

public sealed class PlatformFeeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 9, 0, 0, TimeSpan.Zero);

    private static Order Place(decimal unitPrice, int units, OrderFee fee, LipaMdogoMdogoTerms? installments = null) =>
        Order.Place(
            new OrderId(Guid.CreateVersion7()),
            Guid.CreateVersion7(),
            "7K3M9Q2B",
            new OrderCustomer(Guid.CreateVersion7(), "+254712345678", "Wanjiru"),
            "+254712345678",
            "key-1",
            [OrderLine.Create(new OrderLineId(Guid.CreateVersion7()), new OrderLineSpec(OrderItemKind.Admission, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, "Regular", units, 1, unitPrice))],
            10,
            Now,
            installments,
            fee).Value!;

    [Fact]
    public void FeeIsFiftyPerAdmission()
    {
        var fees = new PlatformFeeOptions();

        Assert.Equal(new OrderFee(150m, false), CheckoutService.TicketFee(fees, 3, waitlisted: false));
    }

    [Fact]
    public void WaitlistedPhonesPayNoFee()
    {
        Assert.Equal(new OrderFee(0m, true), CheckoutService.TicketFee(new PlatformFeeOptions(), 4, waitlisted: true));
    }

    [Fact]
    public void ZeroFeeConfiguredMeansNoFeeAndNoWaiver()
    {
        Assert.Equal(OrderFee.None, CheckoutService.TicketFee(new PlatformFeeOptions { TicketFeePerTicket = 0 }, 2, waitlisted: true));
    }

    [Fact]
    public void TotalIsSubtotalPlusFee()
    {
        var order = Place(1000m, 2, new OrderFee(100m, false));

        Assert.Equal(2000m, order.Subtotal);
        Assert.Equal(100m, order.PlatformFee);
        Assert.Equal(2100m, order.Total);
        Assert.Equal(2100m, order.AmountDueNow);
    }

    [Fact]
    public void LipaMdogoMdogoDepositCarriesTheWholeFee()
    {
        var order = Place(1001m, 1, new OrderFee(50m, false), new LipaMdogoMdogoTerms(50, Now.AddDays(20)));

        // 50% of 1001 rounds up to 501, plus the KES 50 fee.
        Assert.Equal(551m, order.DepositAmount);
        Assert.Equal(1051m, order.Total);
    }

    [Fact]
    public void WaivedFeeIsRecorded()
    {
        var order = Place(1500m, 1, new OrderFee(0m, true));

        Assert.True(order.FeeWaived);
        Assert.Equal(1500m, order.Total);
    }
}
