using Common.Domain.ResultMonad;
using Ticketing.Domain.Common;

namespace Ticketing.Application.Checkout;

public enum CapacityBucketKind
{
    /// <summary>Admissions of one tier on one date (counted per attendee, Spec 08 §8.1).</summary>
    Tier = 1,

    /// <summary>Admissions sold at one sale phase's price, when the phase has an allocation.</summary>
    Phase = 2,

    /// <summary>Bundles of one Couple / Gang offer, when the offer has an allocation.</summary>
    Offer = 3
}

/// <summary>One bucket an order needs <paramref name="Quantity" /> of; <paramref name="Total" /> is the bucket's size now.</summary>
public sealed record CapacityClaim(CapacityBucketKind Kind, Guid RefId, Guid EventId, int Total, int Quantity, string Label);

public sealed record CapacityBucketView(CapacityBucketKind Kind, Guid RefId, int Total, int Held, int Sold)
{
    public int Available => Math.Max(0, Total - Held - Sold);
}

/// <summary>
///     Atomic capacity counters (Spec 08 §8.1): <c>sold + held + n &lt;= total</c> in one statement, so concurrent
///     checkouts can never oversell. Must run inside <see cref="Abstractions.ITicketingUnitOfWork.InTransactionAsync{T}" />.
/// </summary>
public interface ICapacityLedger
{
    /// <summary>Holds every claim for the order, or none (returns <c>Ticketing.SoldOut</c> naming what ran out).</summary>
    Task<Result> HoldAsync(OrderId orderId, IReadOnlyList<CapacityClaim> claims, DateTimeOffset expiresAt, CancellationToken cancellationToken);

    /// <summary>Held → sold for a paid order. Returns <c>false</c> when the order has no live holds (already released).</summary>
    Task<bool> ConvertAsync(OrderId orderId, CancellationToken cancellationToken);

    /// <summary>Gives the order's held capacity back.</summary>
    Task ReleaseAsync(OrderId orderId, CancellationToken cancellationToken);

    /// <summary>
    ///     Late payment: takes the order's released holds again straight as sold, or fails if any bucket no longer has
    ///     room (the caller rolls back).
    /// </summary>
    Task<Result> ReacquireAsSoldAsync(OrderId orderId, CancellationToken cancellationToken);

    /// <summary>Gives a cancelled order's sold capacity back (Lipa Mdogo Mdogo balance never paid).</summary>
    Task ReturnSoldAsync(OrderId orderId, CancellationToken cancellationToken);

    Task<IReadOnlyList<CapacityBucketView>> ListForEventAsync(Guid eventId, CancellationToken cancellationToken);
}
