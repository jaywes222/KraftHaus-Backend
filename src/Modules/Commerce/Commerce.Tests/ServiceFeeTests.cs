using Commerce.Domain.Common;
using Commerce.Domain.Orders;
using Xunit;

namespace Commerce.Tests;

public sealed class ServiceFeeTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 18, 0, 0, TimeSpan.Zero);

    private static KraftOrder Place(decimal commission, decimal serviceFee, bool waived = false) =>
        KraftOrder.Place(
            new KraftOrderId(Guid.CreateVersion7()),
            new KraftOrderSpec(Guid.CreateVersion7(), Guid.CreateVersion7(), "Mama Pima", Guid.CreateVersion7(), KraftOrderKind.Reservation,
                "7QK2MD", "ABCD2345", Guid.CreateVersion7(), "+254700000001", "Wanjiru Kamau", "+254700000001", "key-1",
                commission, Now, Now.AddMinutes(10), serviceFee, waived),
            [KraftOrderLine.Create(new KraftOrderLineId(Guid.CreateVersion7()), Guid.CreateVersion7(), "Pilau", 2, 400m, false)]);

    [Fact]
    public void CustomerPaysTheServiceFeeOnTop()
    {
        var order = Place(10m, 30m);

        Assert.Equal(800m, order.Subtotal);
        Assert.Equal(830m, order.Total);
    }

    [Fact]
    public void PlatformGetsCommissionOnSubtotalPlusServiceFee()
    {
        var order = Place(10m, 30m);

        order.MarkPaid(Guid.NewGuid(), "RCP1", false, Now);

        Assert.Equal(110m, order.PlatformFee);
        Assert.Equal(720m, order.KraftRevenue);
    }

    [Fact]
    public void WaivedOrderHasNoServiceFee()
    {
        var order = Place(0m, 0m, waived: true);

        order.MarkPaid(Guid.NewGuid(), null, false, Now);

        Assert.True(order.ServiceFeeWaived);
        Assert.Equal(800m, order.Total);
        Assert.Equal(0m, order.PlatformFee);
    }
}
