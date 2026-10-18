using Commerce.Application.Settlements;
using Commerce.Domain.Common;
using Commerce.Domain.Menus;
using Commerce.Domain.Orders;
using Commerce.Domain.Settlements;
using Xunit;

namespace Commerce.Tests;

public sealed class MenuExtrasTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 18, 0, 0, TimeSpan.Zero);

    private static MenuItemSpec Spec(MenuCategory category = MenuCategory.Food, IReadOnlyList<MenuAddOnSpec>? addOns = null, DateTimeOffset? startsAt = null) =>
        new("Chips masala", null, category, 300m, null, null, null, 0, addOns, startsAt, startsAt is null ? null : 30);

    [Fact]
    public void AddOnsAreValidatedAndKeepTheirIds()
    {
        var item = MenuItem.Create(new MenuItemId(Guid.NewGuid()), Guid.NewGuid(), Guid.NewGuid(),
            Spec(addOns: [new MenuAddOnSpec(null, "Extra cheese", 50m)])).Value!;
        var cheese = Assert.Single(item.AddOns);

        Assert.False(item.Update(Spec(addOns: [new MenuAddOnSpec(cheese.Id, "Extra cheese", 60m), new MenuAddOnSpec(null, "No chilli", 0m)])).IsFailure);
        Assert.Equal(cheese.Id, item.AddOns[0].Id);
        Assert.Equal(60m, item.AddOns[0].Price);

        Assert.True(item.Update(Spec(addOns: [new MenuAddOnSpec(null, "Same", 1m), new MenuAddOnSpec(null, "same", 2m)])).IsFailure);
        Assert.True(item.Update(Spec(addOns: [new MenuAddOnSpec(null, "Half", 0.5m)])).IsFailure);
    }

    [Fact]
    public void ChoosingAnUnknownAddOnIsRefused()
    {
        var item = MenuItem.Create(new MenuItemId(Guid.NewGuid()), Guid.NewGuid(), Guid.NewGuid(),
            Spec(addOns: [new MenuAddOnSpec(null, "Extra cheese", 50m)])).Value!;

        Assert.Single(item.ChooseAddOns([item.AddOns[0].Id]).Value!);
        Assert.True(item.ChooseAddOns([Guid.NewGuid()]).IsFailure);
        Assert.Empty(item.ChooseAddOns(null).Value!);
    }

    [Fact]
    public void AnActivityNeedsAStartTime()
    {
        Assert.True(MenuItem.Create(new MenuItemId(Guid.NewGuid()), Guid.NewGuid(), Guid.NewGuid(), Spec(MenuCategory.Activity)).IsFailure);

        var ride = MenuItem.Create(new MenuItemId(Guid.NewGuid()), Guid.NewGuid(), Guid.NewGuid(), Spec(MenuCategory.Activity, startsAt: Now)).Value!;
        Assert.True(ride.IsActivity);
        Assert.Equal(Now, ride.StartsAt);
        Assert.False(ride.AgeRestricted);
    }

    [Fact]
    public void AStatementNetsWhatEachSideHolds()
    {
        var kraftId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var platform = Paid(kraftId, eventId, platformTill: true, subtotal: 1000m);
        var own = Paid(kraftId, eventId, platformTill: false, subtotal: 500m);
        var payout = KraftSettlement.Record(new KraftSettlementId(Guid.NewGuid()), kraftId, eventId, SettlementDirection.PlatformPaidKraft,
            400m, "QK7PAY", null, Now, Guid.NewGuid()).Value!;

        var statement = SettlementService.Statement(kraftId, null, eventId, [platform, own], [payout]);

        // Platform holds 1,030 and owes the Kraft 900; the Kraft holds 530 and owes the platform 80 (50 + 30).
        Assert.Equal(1030m, statement.CollectedInPlatformTill);
        Assert.Equal(530m, statement.CollectedInOwnTill);
        Assert.Equal(900m, statement.PlatformOwesKraft);
        Assert.Equal(80m, statement.KraftOwesPlatform);
        Assert.Equal(60m, statement.ServiceFees);
        Assert.Equal(150m, statement.Commission);
        Assert.Equal(900m - 80m - 400m, statement.Balance);
    }

    [Fact]
    public void ASettlementNeedsAReference()
    {
        Assert.True(KraftSettlement.Record(new KraftSettlementId(Guid.NewGuid()), Guid.NewGuid(), Guid.NewGuid(),
            SettlementDirection.KraftPaidPlatform, 100m, " ", null, Now, Guid.NewGuid()).IsFailure);
    }

    private static KraftOrder Paid(Guid kraftId, Guid eventId, bool platformTill, decimal subtotal)
    {
        var order = KraftOrder.Place(
            new KraftOrderId(Guid.CreateVersion7()),
            new KraftOrderSpec(Guid.CreateVersion7(), kraftId, "Mama Pima", eventId, KraftOrderKind.Live, "7QK2MD", "ABCD2345",
                Guid.CreateVersion7(), "+254700000001", "Wanjiru", "+254700000001", "key", 10m, Now, Now.AddMinutes(10), 30m, false, platformTill),
            [KraftOrderLine.Create(new KraftOrderLineId(Guid.CreateVersion7()), Guid.CreateVersion7(), "Item", 1, subtotal, false)]);
        order.MarkPaid(Guid.NewGuid(), "RCP", false, Now);
        return order;
    }
}
