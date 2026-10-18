using Common.Domain.ResultMonad;

namespace Commerce.Application.Stock;

public enum StockKind
{
    /// <summary>Portions ordered live on the day.</summary>
    Live = 1,

    /// <summary>Portions pre-ordered (reserved) before the day.</summary>
    Reservation = 2
}

/// <param name="Total">The bucket's size now; null for no limit.</param>
public sealed record StockClaim(StockKind Kind, Guid MenuItemId, Guid EventId, Guid KraftId, int? Total, int Quantity, string Label);

public sealed record StockLevel(StockKind Kind, Guid MenuItemId, int? Total, int Held, int Sold)
{
    /// <summary>Portions still free; null for no limit.</summary>
    public int? Available => Total is { } total ? Math.Max(0, total - Held - Sold) : null;
}

/// <summary>
///     Atomic stock counters, the same pattern as ticket capacity (Spec 08 §8.1): <c>sold + held + n &lt;= total</c> in one
///     statement, so two buyers can never take the last portion twice. Must run inside
///     <see cref="Abstractions.ICommerceUnitOfWork.InTransactionAsync{T}" />.
/// </summary>
public interface IStockLedger
{
    /// <summary>Holds every claim for the order, or none (returns <c>Commerce.SoldOut</c> naming what ran out).</summary>
    Task<Result> HoldAsync(Guid orderId, IReadOnlyList<StockClaim> claims, DateTimeOffset expiresAt, CancellationToken cancellationToken);

    /// <summary>Held → sold for a paid order. False when the order has no live holds (already released).</summary>
    Task<bool> ConvertAsync(Guid orderId, CancellationToken cancellationToken);

    Task ReleaseAsync(Guid orderId, CancellationToken cancellationToken);

    /// <summary>Late payment: takes the released portions again straight as sold, or fails when they are gone.</summary>
    Task<Result> ReacquireAsSoldAsync(Guid orderId, CancellationToken cancellationToken);

    Task<IReadOnlyList<StockLevel>> ListAsync(IReadOnlyCollection<Guid> menuItemIds, CancellationToken cancellationToken);
}
