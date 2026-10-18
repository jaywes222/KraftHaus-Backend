using Common.Domain.ResultMonad;
using Ticketing.Domain.Common;
using Ticketing.Domain.Orders;
using Ticketing.Domain.Tickets;
using Ticketing.Domain.Waitlist;

namespace Ticketing.Application.Abstractions;

/// <summary>Waitlist entries in the resolved tenant's database.</summary>
public interface IWaitlistRepository
{
    /// <summary>True when <paramref name="phone" /> (E.164) is already on the event's waitlist.</summary>
    Task<bool> ExistsAsync(Guid eventId, string phone, CancellationToken cancellationToken);

    Task<WaitlistEntry?> FindAsync(WaitlistEntryId id, CancellationToken cancellationToken);

    Task<int> CountAsync(Guid eventId, CancellationToken cancellationToken);

    Task<IReadOnlyList<WaitlistEntry>> ListForReadAsync(
        Guid eventId,
        DateTimeOffset? joinedAfter,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Entries whose confirmation SMS has not been accepted yet, joined in the window, below the attempt limit.</summary>
    Task<IReadOnlyList<WaitlistEntry>> ListUnconfirmedAsync(
        DateTimeOffset joinedBefore,
        DateTimeOffset joinedAfter,
        int maxAttempts,
        int limit,
        CancellationToken cancellationToken);

    void Add(WaitlistEntry entry);
}

public interface IOrderRepository
{
    Task<Order?> FindAsync(OrderId id, CancellationToken cancellationToken);

    Task<Order?> FindForReadAsync(OrderId id, CancellationToken cancellationToken);

    Task<Order?> FindByIdempotencyKeyAsync(Guid customerUserId, string key, CancellationToken cancellationToken);

    /// <summary>Orders of this customer for this event still waiting for payment with a live hold.</summary>
    Task<IReadOnlyList<Order>> ListOpenForCustomerAsync(
        Guid customerUserId,
        Guid eventId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>Orders still awaiting payment whose hold ran out, oldest first.</summary>
    Task<IReadOnlyList<Order>> ListHoldExpiredAsync(DateTimeOffset now, int max, CancellationToken cancellationToken);

    Task<IReadOnlyList<Order>> ListForEventAsync(
        Guid eventId,
        OrderStatus? status,
        bool needsActionOnly,
        DateTimeOffset? placedBefore,
        int limit,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Order>> ListForCustomerAsync(Guid customerUserId, int limit, CancellationToken cancellationToken);

    /// <summary>Money on paid and part-paid orders of the event (admin sales header).</summary>
    Task<OrderTotals> TotalsForEventAsync(Guid eventId, CancellationToken cancellationToken);

    Task<bool> ReferenceExistsAsync(string reference, CancellationToken cancellationToken);

    /// <summary>Lipa Mdogo Mdogo orders still owing their balance, due before <paramref name="dueBefore" />.</summary>
    Task<IReadOnlyList<Order>> ListBalanceDueAsync(DateTimeOffset dueBefore, int max, CancellationToken cancellationToken);

    void Add(Order order);
}

public interface ITicketRepository
{
    Task<Ticket?> FindAsync(TicketId id, CancellationToken cancellationToken);

    Task<Ticket?> FindForReadAsync(TicketId id, CancellationToken cancellationToken);

    /// <summary>The ticket with this (normalised) manual check-in code.</summary>
    Task<Ticket?> FindByCodeAsync(string code, CancellationToken cancellationToken);

    /// <summary>Door search on one date: code prefix, last digits of the holder's phone, or part of the holder's name.</summary>
    Task<IReadOnlyList<Ticket>> SearchAsync(Guid eventDateId, string query, int limit, CancellationToken cancellationToken);

    /// <summary>Count of tickets per status for one date.</summary>
    Task<IReadOnlyDictionary<TicketStatus, int>> CountByStatusAsync(Guid eventDateId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Ticket>> ListForHolderAsync(string phone, CancellationToken cancellationToken);

    /// <summary>True when the phone holds a Valid or CheckedIn ticket for the event.</summary>
    Task<bool> HoldsUsableTicketAsync(string phone, Guid eventId, CancellationToken cancellationToken);

    /// <summary>True when the phone holds a Valid or CheckedIn ticket for that event date.</summary>
    Task<bool> HoldsUsableTicketForDateAsync(string phone, Guid eventDateId, CancellationToken cancellationToken);

    Task<IReadOnlyList<Ticket>> ListForOrderAsync(OrderId orderId, CancellationToken cancellationToken);

    Task<int> CountForOrderAsync(OrderId orderId, CancellationToken cancellationToken);

    /// <summary>
    ///     Admin list of the event's tickets, newest first: optional date, status and search (code prefix, last phone
    ///     digits or part of the holder's or attendee's name), paged with <paramref name="issuedBefore" />.
    /// </summary>
    Task<IReadOnlyList<Ticket>> ListForEventAsync(
        Guid eventId,
        Guid? eventDateId,
        TicketStatus? status,
        string? query,
        DateTimeOffset? issuedBefore,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Count of the event's tickets per status (all dates).</summary>
    Task<IReadOnlyDictionary<TicketStatus, int>> CountByStatusForEventAsync(Guid eventId, CancellationToken cancellationToken);

    void AddRange(IEnumerable<Ticket> tickets);
}

/// <param name="PaidOrders">Orders fully paid or with a deposit paid.</param>
/// <param name="AmountCollected">Money received (deposits count as paid so far).</param>
/// <param name="PlatformFees">Fees on those orders (charged with the first payment).</param>
/// <param name="FeesWaived">Orders whose fee was waived for the waitlist.</param>
public sealed record OrderTotals(int PaidOrders, decimal AmountCollected, decimal PlatformFees, int FeesWaived);

public interface ITicketingUnitOfWork
{
    /// <summary>
    ///     Saves pending changes. A concurrent join with the same phone for the same event (the unique index) comes
    ///     back as <see cref="TicketingErrors.WaitlistAlreadyJoined" /> instead of an exception.
    /// </summary>
    Task<Result> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     Runs <paramref name="work" /> in one database transaction together with the capacity ledger's SQL. Commits
    ///     when it succeeds, rolls back (and detaches pending changes) when it returns a failure or throws.
    /// </summary>
    Task<Result<T>> InTransactionAsync<T>(Func<CancellationToken, Task<Result<T>>> work, CancellationToken cancellationToken);

    /// <summary>True when this consumer already handled <paramref name="messageId" /> (outbox re-delivery).</summary>
    Task<bool> AlreadyProcessedAsync(Guid messageId, string handler, CancellationToken cancellationToken);

    /// <summary>Records <paramref name="messageId" /> as handled; written by the next save, in the same transaction.</summary>
    void MarkProcessed(Guid messageId, string handler);
}
