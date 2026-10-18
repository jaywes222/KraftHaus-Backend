using System.Globalization;
using Common.Application.EventCatalog;
using Common.Domain.ResultMonad;
using Ticketing.Domain.Common;
using Ticketing.Domain.Orders;

namespace Ticketing.Application.Checkout;

/// <summary>One requested item: admissions of a tier (<c>TierId</c>) or bundles of an offer (<c>OfferId</c>).</summary>
public sealed record CheckoutItem(Guid? TierId, Guid? OfferId, int Quantity);

public sealed record PricedOrder(
    IReadOnlyList<OrderLineSpec> Lines,
    IReadOnlyList<CapacityClaim> Claims,
    decimal Total,
    int Admissions,
    bool RequiresEarlyAccess);

/// <summary>
///     Prices a checkout against the event's sale catalogue at <c>now</c>: the tier's current phase price for single
///     admissions, the offer price for Couple / Gang bundles. Pure: no I/O, so every rule is unit-tested.
/// </summary>
public static class CheckoutPricing
{
    public const int MaxItems = 20;
    public const int MaxQuantityPerItem = 50;

    public static Result<PricedOrder> Price(EventSaleSnapshot @event, IReadOnlyList<CheckoutItem> items, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentNullException.ThrowIfNull(items);

        if (@event.Status != EventLifecycle.Published)
        {
            return TicketingErrors.Conflict("EventNotOnSale", "Tickets for this event are not on sale.");
        }

        var errors = new List<string>();

        if (items.Count is 0 or > MaxItems)
        {
            return TicketingErrors.Invalid("OrderInvalid", [$"Items: Choose between 1 and {MaxItems} items."]);
        }

        var lines = new List<OrderLineSpec>();
        var claims = new List<CapacityClaim>();
        var requiresEarlyAccess = false;
        var multipleDates = @event.Dates.Count(x => !x.Cancelled) > 1;

        foreach (var (item, index) in items.Select((x, i) => (x, i)))
        {
            if (item.Quantity is < 1 or > MaxQuantityPerItem || item.TierId.HasValue == item.OfferId.HasValue)
            {
                errors.Add($"Items[{index}]: Give either a tier or an offer, and a quantity between 1 and {MaxQuantityPerItem}.");
                continue;
            }

            var found = Find(@event, item);
            if (found is null)
            {
                errors.Add($"Items[{index}]: This ticket is not part of the event.");
                continue;
            }

            var (date, tier, offer) = found.Value;

            if (date.Cancelled || tier.Withdrawn || now >= date.EndsAt)
            {
                return TicketingErrors.Conflict("NotOnSale", $"{tier.Name} is not on sale.");
            }

            var phase = tier.Phases.FirstOrDefault(x => x.IsOpenAt(now));
            var dateName = multipleDates ? $" · {date.Label ?? date.StartsAt.ToString("ddd d MMM", CultureInfo.InvariantCulture)}" : string.Empty;

            if (offer is null)
            {
                if (phase is null)
                {
                    return TicketingErrors.Conflict("NotOnSale", $"{tier.Name} is not on sale right now.");
                }

                requiresEarlyAccess |= phase.EarlyAccess;

                lines.Add(new OrderLineSpec(
                    OrderItemKind.Admission, date.DateId, tier.TierId, phase.PhaseId, null,
                    $"{tier.Name} · {phase.Name}{dateName}", item.Quantity, 1, phase.Price));

                claims.Add(new CapacityClaim(CapacityBucketKind.Tier, tier.TierId, @event.EventId, tier.Capacity, item.Quantity, tier.Name));

                if (phase.Allocation is { } allocation)
                {
                    claims.Add(new CapacityClaim(CapacityBucketKind.Phase, phase.PhaseId, @event.EventId, allocation, item.Quantity, $"{tier.Name} · {phase.Name}"));
                }
            }
            else
            {
                if (!offer.IsOpenAt(now))
                {
                    return TicketingErrors.Conflict("NotOnSale", $"{offer.Name} is not on sale right now.");
                }

                // An offer sold while the tier is in an early-access phase is early access too.
                requiresEarlyAccess |= phase?.EarlyAccess == true;

                lines.Add(new OrderLineSpec(
                    OrderItemKind.Bundle, date.DateId, tier.TierId, null, offer.OfferId,
                    $"{tier.Name} · {offer.Name}{dateName}", item.Quantity, offer.Admits, offer.Price));

                claims.Add(new CapacityClaim(CapacityBucketKind.Tier, tier.TierId, @event.EventId, tier.Capacity, item.Quantity * offer.Admits, tier.Name));

                if (offer.Allocation is { } allocation)
                {
                    claims.Add(new CapacityClaim(CapacityBucketKind.Offer, offer.OfferId, @event.EventId, allocation, item.Quantity, offer.Name));
                }
            }
        }

        if (errors.Count > 0)
        {
            return TicketingErrors.Invalid("OrderInvalid", errors);
        }

        var admissions = lines.Sum(x => x.Units * x.AdmissionsPerUnit);
        if (admissions > @event.Policies.MaxTicketsPerOrder)
        {
            return TicketingErrors.Invalid(
                "TooManyTickets",
                [$"Items: At most {@event.Policies.MaxTicketsPerOrder} tickets per order."]);
        }

        // One hold per bucket per order: merge claims that hit the same bucket.
        var merged = claims
            .GroupBy(x => (x.Kind, x.RefId))
            .Select(g => g.First() with { Quantity = g.Sum(x => x.Quantity) })
            .ToList();

        return new PricedOrder(lines, merged, lines.Sum(x => x.UnitPrice * x.Units), admissions, requiresEarlyAccess);
    }

    private static (SaleDate Date, SaleTier Tier, SaleOffer? Offer)? Find(EventSaleSnapshot @event, CheckoutItem item)
    {
        foreach (var date in @event.Dates)
        {
            foreach (var tier in date.Tiers)
            {
                if (item.TierId == tier.TierId)
                {
                    return (date, tier, null);
                }

                if (item.OfferId is { } offerId && tier.Offers.FirstOrDefault(x => x.OfferId == offerId) is { } offer)
                {
                    return (date, tier, offer);
                }
            }
        }

        return null;
    }
}
