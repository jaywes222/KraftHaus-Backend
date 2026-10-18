using Common.Application.EventCatalog;
using Ticketing.Application.Checkout;
using Ticketing.Domain.Orders;
using Xunit;

namespace Ticketing.Tests;

public sealed class CheckoutPricingTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Tier = Guid.CreateVersion7();
    private static readonly Guid EarlyPhase = Guid.CreateVersion7();
    private static readonly Guid Couple = Guid.CreateVersion7();

    [Fact]
    public void AdmissionsUseTheCurrentPhasePriceBundlesTheOfferPrice()
    {
        var priced = CheckoutPricing.Price(Event(), [new CheckoutItem(Tier, null, 2), new CheckoutItem(null, Couple, 1)], Now).Value!;

        Assert.Equal(2 * 1500m + 2800m, priced.Total);
        Assert.Equal(4, priced.Admissions);
        Assert.Equal(OrderItemKind.Bundle, priced.Lines[1].Kind);
        Assert.False(priced.RequiresEarlyAccess);

        // Tier claims merged into one hold (2 singles + 2 from the couple); phase and offer allocations claimed separately.
        Assert.Equal(4, priced.Claims.Single(x => x.Kind == CapacityBucketKind.Tier).Quantity);
        Assert.Equal(2, priced.Claims.Single(x => x.Kind == CapacityBucketKind.Phase).Quantity);
        Assert.Equal(1, priced.Claims.Single(x => x.Kind == CapacityBucketKind.Offer).Quantity);
    }

    [Fact]
    public void LaterTheNextPhaseApplies()
    {
        var priced = CheckoutPricing.Price(Event(), [new CheckoutItem(Tier, null, 1)], Now.AddDays(8)).Value!;

        Assert.Equal(2000m, priced.Total);
        Assert.DoesNotContain(priced.Claims, x => x.Kind == CapacityBucketKind.Phase);
    }

    [Fact]
    public void EarlyAccessPhaseMarksTheOrder()
    {
        var priced = CheckoutPricing.Price(Event(earlyAccess: true), [new CheckoutItem(null, Couple, 1)], Now).Value!;

        Assert.True(priced.RequiresEarlyAccess);
    }

    [Fact]
    public void OrderSizeIsCappedByThePolicy()
    {
        var result = CheckoutPricing.Price(Event(), [new CheckoutItem(null, Couple, 3), new CheckoutItem(Tier, null, 5)], Now);

        Assert.Equal("Ticketing.TooManyTickets", result.Error!.Key);
    }

    [Fact]
    public void NotOnSaleCases()
    {
        Assert.Equal("Ticketing.NotOnSale", CheckoutPricing.Price(Event(), [new CheckoutItem(Tier, null, 1)], Now.AddDays(40)).Error!.Key);
        Assert.Equal("Ticketing.EventNotOnSale", CheckoutPricing.Price(Event(status: EventLifecycle.Cancelled), [new CheckoutItem(Tier, null, 1)], Now).Error!.Key);
        Assert.Equal("Ticketing.NotOnSale", CheckoutPricing.Price(Event(withdrawn: true), [new CheckoutItem(Tier, null, 1)], Now).Error!.Key);
    }

    [Fact]
    public void BadItemsAreReportedPerIndex()
    {
        var result = CheckoutPricing.Price(Event(), [new CheckoutItem(Tier, Couple, 1), new CheckoutItem(Guid.NewGuid(), null, 1), new CheckoutItem(Tier, null, 0)], Now);

        Assert.Equal("Ticketing.OrderInvalid", result.Error!.Key);
        Assert.Equal(["Items[0]", "Items[1]", "Items[2]"], result.Error.SubErrors!.Select(x => x.Split(':')[0]));
    }

    private static EventSaleSnapshot Event(bool earlyAccess = false, EventLifecycle status = EventLifecycle.Published, bool withdrawn = false) =>
        new(
            Guid.CreateVersion7(),
            "krafthaus-001",
            "KraftHaus 001",
            "KICC",
            status,
            new EventSalePolicies(false, true, 1, true, 10, 8),
            [
                new SaleDate(Guid.CreateVersion7(), "Day 1", Now.AddDays(20), Now.AddDays(20).AddHours(8), null, false,
                [
                    new SaleTier(Tier, "Regular", 500, withdrawn,
                    [
                        new SalePhaseInfo(EarlyPhase, "Early Bird", 1500m, Now.AddDays(-1), Now.AddDays(7), 100, earlyAccess),
                        new SalePhaseInfo(Guid.CreateVersion7(), "Advance", 2000m, Now.AddDays(7), Now.AddDays(20), null, false)
                    ],
                    [
                        new SaleOffer(Couple, "Couple ticket", 2, 2800m, Now.AddDays(-1), Now.AddDays(20), 50, false)
                    ])
                ])
            ]);
}
