using Commerce.Application;
using Commerce.Application.Menus;
using Commerce.Domain.Common;
using Commerce.Domain.Menus;
using Commerce.Domain.Orders;
using Common.Application.EventCatalog;
using Xunit;

namespace Commerce.Tests;

public sealed class OrderAndMenuTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TheFeeIsFixedWhenTheOrderIsPaid()
    {
        var order = NewOrder(commission: 12.5m, (999m, 1));

        order.MarkPaid(Guid.CreateVersion7(), "TST123", late: false, Now);

        Assert.Equal(124.88m, order.PlatformFee);
        Assert.Equal(874.12m, order.KraftRevenue);
        Assert.Equal(KraftOrderStatus.Paid, order.Status);
    }

    [Fact]
    public void AnOrderMovesThroughTheKitchenInOrder()
    {
        var order = NewOrder(10m, (500m, 2));

        Assert.Equal("Commerce.OrderNotNew", order.Accept(Now).Error!.Key);
        order.MarkPaid(Guid.CreateVersion7(), null, false, Now);

        Assert.Equal("Commerce.OrderNotReady", order.Collect(Guid.CreateVersion7(), Now).Error!.Key);
        Assert.False(order.Accept(Now).IsFailure);
        Assert.False(order.MarkReady(Now).IsFailure);
        Assert.False(order.Collect(Guid.CreateVersion7(), Now).IsFailure);
        Assert.Equal("Commerce.AlreadyCollected", order.Collect(Guid.CreateVersion7(), Now).Error!.Key);
    }

    [Fact]
    public void ALineThatRanOutIsOwedBackAndTheOrderWaitsForFinance()
    {
        var order = NewOrder(10m, (500m, 2), (200m, 1));
        order.MarkPaid(Guid.CreateVersion7(), null, false, Now);
        var line = order.Lines[1];

        Assert.Equal("Commerce.LineUnavailableInvalid", order.MarkLineUnavailable(line.Id, " ", Now).Error!.Key);
        Assert.False(order.MarkLineUnavailable(line.Id, "Ran out", Now).IsFailure);

        Assert.True(order.NeedsAction);
        Assert.Equal(200m, order.AmountToRefund);
        Assert.Equal("Commerce.LineAlreadyUnavailable", order.MarkLineUnavailable(line.Id, "again", Now).Error!.Key);

        Assert.False(order.ResolveAttention("Refunded by reversal", Guid.CreateVersion7(), Now).IsFailure);
        Assert.False(order.NeedsAction);
        Assert.Equal("Commerce.NothingToResolve", order.ResolveAttention("again", Guid.CreateVersion7(), Now).Error!.Key);
    }

    [Fact]
    public void AnOrderWithEveryLineUnavailableCannotBeMarkedReady()
    {
        var order = NewOrder(0m, (300m, 1));
        order.MarkPaid(Guid.CreateVersion7(), null, false, Now);
        order.MarkLineUnavailable(order.Lines[0].Id, "Gone", Now);

        Assert.Equal("Commerce.NothingToHandOver", order.MarkReady(Now).Error!.Key);
    }

    [Fact]
    public void PaymentAttemptsStopAtTheLimitAndAfterTheHold()
    {
        var order = NewOrder(0m, (300m, 1));

        Assert.False(order.StartPayment(Guid.CreateVersion7(), "+254700000001", 2, Now).IsFailure);
        Assert.False(order.StartPayment(Guid.CreateVersion7(), "+254700000001", 2, Now).IsFailure);
        Assert.Equal("Commerce.TooManyPaymentAttempts", order.StartPayment(Guid.CreateVersion7(), "+254700000001", 2, Now).Error!.Key);

        var expired = NewOrder(0m, (300m, 1));
        Assert.Equal("Commerce.OrderExpired", expired.StartPayment(Guid.CreateVersion7(), "+254700000001", 5, Now.AddHours(1)).Error!.Key);
    }

    [Fact]
    public void AFailureOfAnOlderPromptIsIgnored()
    {
        var order = NewOrder(0m, (300m, 1));
        var first = Guid.CreateVersion7();
        order.StartPayment(first, "+254700000001", 5, Now);
        order.StartPayment(Guid.CreateVersion7(), "+254700000001", 5, Now);

        order.PaymentFailed(first, "Cancelled");

        Assert.Null(order.LastPaymentFailure);
    }

    [Theory]
    [InlineData(0.5, null, null, "Price")]
    [InlineData(100, -1, null, "LiveStock")]
    [InlineData(100, 0, null, "LiveStock")]
    [InlineData(100, null, -2, "ReservationAllocation")]
    public void MenuItemsNeedAPayablePriceAndAWayToSell(double price, int? live, int? reserved, string field)
    {
        var result = MenuItem.Create(new MenuItemId(Guid.CreateVersion7()), Guid.CreateVersion7(), Guid.CreateVersion7(),
            new MenuItemSpec("Chapati", null, MenuCategory.Food, (decimal)price, live, reserved, null, 0));

        Assert.Equal("Commerce.MenuItemInvalid", result.Error!.Key);
        Assert.Contains(result.Error.SubErrors!, x => x.StartsWith(field + ":", StringComparison.Ordinal));
    }

    [Fact]
    public void DrinksAreAgeRestrictedUnlessSaidOtherwise()
    {
        var beer = Item(MenuCategory.Drink, null);
        var soda = Item(MenuCategory.Drink, false);
        var food = Item(MenuCategory.Food, null);

        Assert.True(beer.AgeRestricted);
        Assert.False(soda.AgeRestricted);
        Assert.False(food.AgeRestricted);
    }

    [Fact]
    public void ARetiredItemCannotBeSwitchedBackOn()
    {
        var item = Item(MenuCategory.Food, null);

        Assert.False(item.Retire().IsFailure);
        Assert.False(item.IsOrderable);
        Assert.Equal("Commerce.MenuItemRetired", item.SetAvailable(true).Error!.Key);
    }

    [Fact]
    public void ReservationsCloseBeforeTheDayAndLiveOrderingRunsUntilTheEnd()
    {
        var start = Now;
        var snapshot = new EventSnapshot(Guid.CreateVersion7(), "kh-001", "KraftHaus 001", EventLifecycle.Published, start, start.AddHours(8));
        var windows = OrderingWindows.For(snapshot, new CommerceOptions { ReservationsCloseHoursBeforeStart = 2, LiveOrderingOpensMinutesBeforeStart = 60 });

        Assert.True(windows.ReservationsOpenAt(start.AddHours(-3), EventLifecycle.Published));
        Assert.False(windows.ReservationsOpenAt(start.AddHours(-2), EventLifecycle.Published));
        Assert.False(windows.LiveOpenAt(start.AddMinutes(-61), EventLifecycle.Published));
        Assert.True(windows.LiveOpenAt(start.AddMinutes(-60), EventLifecycle.Published));
        Assert.True(windows.LiveOpenAt(start.AddHours(7), EventLifecycle.Published));
        Assert.False(windows.LiveOpenAt(start.AddHours(8), EventLifecycle.Published));
        Assert.False(windows.LiveOpenAt(start, EventLifecycle.Cancelled));
    }

    [Fact]
    public void LiveOrdersFollowTheDateThatIsOn()
    {
        var options = new CommerceOptions { LiveOrderingOpensMinutesBeforeStart = 60 };
        var day1 = Date(Now, Now.AddHours(8));
        var day2 = Date(Now.AddDays(1), Now.AddDays(1).AddHours(8));
        var cancelled = Date(Now.AddDays(2), Now.AddDays(2).AddHours(8), cancelled: true);
        SaleDate[] dates = [day1, day2, cancelled];

        Assert.Equal(day1.DateId, OrderingWindows.LiveDateAt(dates, Now.AddMinutes(-60), options));
        Assert.Equal(day1.DateId, OrderingWindows.LiveDateAt(dates, Now.AddHours(7), options));
        Assert.Null(OrderingWindows.LiveDateAt(dates, Now.AddHours(12), options));
        Assert.Equal(day2.DateId, OrderingWindows.LiveDateAt(dates, Now.AddDays(1).AddMinutes(-30), options));
        Assert.Null(OrderingWindows.LiveDateAt(dates, Now.AddDays(2).AddHours(1), options));
    }

    [Fact]
    public void WhenDateWindowsOverlapTheLaterDateWins()
    {
        var options = new CommerceOptions { LiveOrderingOpensMinutesBeforeStart = 120 };
        var evening = Date(Now, Now.AddHours(5));
        var late = Date(Now.AddHours(4), Now.AddHours(9));

        Assert.Equal(late.DateId, OrderingWindows.LiveDateAt([evening, late], Now.AddHours(3), options));
    }

    private static SaleDate Date(DateTimeOffset start, DateTimeOffset end, bool cancelled = false) =>
        new(Guid.CreateVersion7(), null, start, end, null, cancelled, []);

    private static MenuItem Item(MenuCategory category, bool? ageRestricted) =>
        MenuItem.Create(new MenuItemId(Guid.CreateVersion7()), Guid.CreateVersion7(), Guid.CreateVersion7(),
            new MenuItemSpec("Item", null, category, 250m, null, null, ageRestricted, 0)).Value!;

    private static KraftOrder NewOrder(decimal commission, params (decimal Price, int Quantity)[] lines) =>
        KraftOrder.Place(
            new KraftOrderId(Guid.CreateVersion7()),
            new KraftOrderSpec(Guid.CreateVersion7(), Guid.CreateVersion7(), "Mama Pima", Guid.CreateVersion7(), KraftOrderKind.Live,
                "7QK2MD", "ABCD2345", Guid.CreateVersion7(), "+254700000001", "Wanjiru Kamau", "+254700000001", "key-1",
                commission, Now, Now.AddMinutes(10)),
            [
                .. lines.Select((x, i) => KraftOrderLine.Create(new KraftOrderLineId(Guid.CreateVersion7()), Guid.CreateVersion7(), $"Item {i}", x.Quantity, x.Price, false))
            ]);
}
