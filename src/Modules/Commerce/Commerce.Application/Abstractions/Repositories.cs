using Commerce.Domain.Common;
using Commerce.Domain.Krafts;
using Commerce.Domain.Menus;
using Commerce.Domain.Orders;
using Common.Domain.ResultMonad;

namespace Commerce.Application.Abstractions;

/// <summary>Krafts in the resolved tenant's database.</summary>
public interface IKraftRepository
{
    Task<Kraft?> FindAsync(KraftId id, CancellationToken cancellationToken);

    Task<Kraft?> FindForReadAsync(KraftId id, CancellationToken cancellationToken);

    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken);

    /// <summary>Krafts by name; only those signed up for <paramref name="eventId" /> when given.</summary>
    Task<IReadOnlyList<Kraft>> ListForReadAsync(KraftStatus? status, Guid? eventId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Kraft>> ListByIdsForReadAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    void Add(Kraft kraft);
}

public interface IMenuItemRepository
{
    Task<MenuItem?> FindAsync(MenuItemId id, CancellationToken cancellationToken);

    /// <summary>A Kraft's items, optionally for one event, retired ones only when asked.</summary>
    Task<IReadOnlyList<MenuItem>> ListForKraftAsync(Guid kraftId, Guid? eventId, bool includeRetired, CancellationToken cancellationToken);

    /// <summary>Active items of an event.</summary>
    Task<IReadOnlyList<MenuItem>> ListActiveForEventAsync(Guid eventId, CancellationToken cancellationToken);

    Task<IReadOnlyList<MenuItem>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    void Add(MenuItem item);
}

public interface IKraftOrderRepository
{
    Task<KraftOrder?> FindAsync(KraftOrderId id, CancellationToken cancellationToken);

    Task<KraftOrder?> FindForReadAsync(KraftOrderId id, CancellationToken cancellationToken);

    /// <summary>The orders a customer placed with this Idempotency-Key (one checkout), tracked.</summary>
    Task<IReadOnlyList<KraftOrder>> ListByIdempotencyKeyAsync(Guid customerUserId, string key, CancellationToken cancellationToken);

    Task<IReadOnlyList<KraftOrder>> ListGroupAsync(Guid checkoutGroupId, CancellationToken cancellationToken);

    Task<IReadOnlyList<KraftOrder>> ListForCustomerAsync(Guid customerUserId, int limit, CancellationToken cancellationToken);

    /// <summary>Orders still waiting for payment whose hold ran out, oldest first (tracked).</summary>
    Task<IReadOnlyList<KraftOrder>> ListHoldExpiredAsync(DateTimeOffset now, int max, CancellationToken cancellationToken);

    /// <summary>A Kraft's kitchen board: paid, preparing and ready orders, plus those collected since <paramref name="collectedSince" />.</summary>
    Task<IReadOnlyList<KraftOrder>> ListBoardAsync(Guid kraftId, Guid? eventId, DateTimeOffset collectedSince, CancellationToken cancellationToken);

    /// <summary>Orders of the event waiting at the stall (Ready), oldest first; <paramref name="kraftIds" /> empty for all.</summary>
    Task<IReadOnlyList<KraftOrder>> ListReadyForEventAsync(Guid eventId, IReadOnlyCollection<Guid> kraftIds, int limit, CancellationToken cancellationToken);

    Task<KraftOrder?> FindByPickupCodeAsync(Guid kraftId, string code, CancellationToken cancellationToken);

    /// <summary>Ready orders not collected since before <paramref name="readyBefore" /> and not flagged yet (tracked).</summary>
    Task<IReadOnlyList<KraftOrder>> ListUncollectedAsync(DateTimeOffset readyBefore, int max, CancellationToken cancellationToken);

    Task<IReadOnlyList<KraftOrder>> ListForAdminAsync(
        Guid? eventId,
        Guid? kraftId,
        KraftOrderStatus? status,
        bool needsActionOnly,
        DateTimeOffset? placedBefore,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Every paid (or later) order of an event, optionally of one Kraft, for sales totals.</summary>
    Task<IReadOnlyList<KraftOrder>> ListPaidAsync(Guid eventId, Guid? kraftId, CancellationToken cancellationToken);

    Task<bool> ReferenceExistsAsync(string reference, CancellationToken cancellationToken);

    void AddRange(IEnumerable<KraftOrder> orders);
}

public interface ICommerceUnitOfWork
{
    /// <summary>Saves; a lost optimistic-concurrency race comes back as <see cref="CommerceErrors.ConcurrentUpdate" />.</summary>
    Task<Result> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Runs <paramref name="work" /> in one database transaction; rolled back when it fails or throws.</summary>
    Task<Result<T>> InTransactionAsync<T>(Func<CancellationToken, Task<Result<T>>> work, CancellationToken cancellationToken);

    Task<bool> AlreadyProcessedAsync(Guid messageId, string handler, CancellationToken cancellationToken);

    void MarkProcessed(Guid messageId, string handler);
}
