using Common.Domain.ResultMonad;
using Common.Infrastructure.Messaging;
using EntityFramework.Exceptions.Common;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions;
using Ticketing.Domain.Common;
using Ticketing.Domain.Orders;
using Ticketing.Domain.Tickets;
using Ticketing.Domain.Waitlist;
using Ticketing.Infrastructure.Persistence.Configurations;
using Common.Domain.Codes;

namespace Ticketing.Infrastructure.Persistence.Repositories;

internal sealed class WaitlistRepository(TicketingDbContext db) : IWaitlistRepository
{
    public Task<bool> ExistsAsync(Guid eventId, string phone, CancellationToken cancellationToken) =>
        db.WaitlistEntries
            .AsNoTracking()
            .TagWith(nameof(ExistsAsync))
            .AnyAsync(x => x.EventId == eventId && x.Phone == phone, cancellationToken);

    public Task<WaitlistEntry?> FindAsync(WaitlistEntryId id, CancellationToken cancellationToken) =>
        db.WaitlistEntries
            .TagWith(nameof(FindAsync))
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<int> CountAsync(Guid eventId, CancellationToken cancellationToken) =>
        db.WaitlistEntries.AsNoTracking().CountAsync(x => x.EventId == eventId, cancellationToken);

    public async Task<IReadOnlyList<WaitlistEntry>> ListForReadAsync(
        Guid eventId,
        DateTimeOffset? joinedAfter,
        int limit,
        CancellationToken cancellationToken) =>
        await db.WaitlistEntries
            .AsNoTracking()
            .Where(x => x.EventId == eventId && (joinedAfter == null || x.JoinedAt > joinedAfter))
            .OrderBy(x => x.JoinedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<WaitlistEntry>> ListUnconfirmedAsync(
        DateTimeOffset joinedBefore,
        DateTimeOffset joinedAfter,
        int maxAttempts,
        int limit,
        CancellationToken cancellationToken) =>
        await db.WaitlistEntries
            .TagWith(nameof(ListUnconfirmedAsync))
            .Where(x => x.ConfirmationSentAt == null && x.ConfirmationAttempts < maxAttempts &&
                        x.JoinedAt <= joinedBefore && x.JoinedAt >= joinedAfter)
            .OrderBy(x => x.JoinedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public void Add(WaitlistEntry entry) => db.WaitlistEntries.Add(entry);
}

internal sealed class OrderRepository(TicketingDbContext db) : IOrderRepository
{
    public Task<Order?> FindAsync(OrderId id, CancellationToken cancellationToken) =>
        db.Orders.TagWith(nameof(FindAsync)).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<Order?> FindForReadAsync(OrderId id, CancellationToken cancellationToken) =>
        db.Orders.AsNoTracking().TagWith(nameof(FindForReadAsync)).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<Order?> FindByIdempotencyKeyAsync(Guid customerUserId, string key, CancellationToken cancellationToken) =>
        db.Orders.TagWith(nameof(FindByIdempotencyKeyAsync))
            .SingleOrDefaultAsync(x => x.CustomerUserId == customerUserId && x.IdempotencyKey == key, cancellationToken);

    public async Task<IReadOnlyList<Order>> ListOpenForCustomerAsync(
        Guid customerUserId,
        Guid eventId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        await db.Orders.AsNoTracking()
            .Where(x => x.CustomerUserId == customerUserId && x.EventId == eventId &&
                        x.Status == OrderStatus.AwaitingPayment && x.HoldExpiresAt > now)
            .OrderBy(x => x.PlacedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Order>> ListHoldExpiredAsync(DateTimeOffset now, int max, CancellationToken cancellationToken) =>
        await db.Orders.TagWith(nameof(ListHoldExpiredAsync))
            .Where(x => x.Status == OrderStatus.AwaitingPayment && x.HoldExpiresAt <= now)
            .OrderBy(x => x.HoldExpiresAt)
            .Take(max)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Order>> ListForEventAsync(
        Guid eventId,
        OrderStatus? status,
        bool needsActionOnly,
        DateTimeOffset? placedBefore,
        int limit,
        CancellationToken cancellationToken) =>
        await db.Orders.AsNoTracking()
            .Where(x => x.EventId == eventId &&
                        (status == null || x.Status == status) &&
                        (!needsActionOnly || (x.AttentionReason != null && x.AttentionResolvedAt == null)) &&
                        (placedBefore == null || x.PlacedAt < placedBefore))
            .OrderByDescending(x => x.PlacedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task<OrderTotals> TotalsForEventAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var rows = await db.Orders.AsNoTracking()
            .TagWith(nameof(TotalsForEventAsync))
            .Where(x => x.EventId == eventId && (x.Status == OrderStatus.Paid || x.Status == OrderStatus.PartiallyPaid))
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Count = g.Count(),
                Collected = g.Sum(x => x.AmountPaid),
                Fees = g.Sum(x => x.PlatformFee),
                Waived = g.Count(x => x.FeeWaived)
            })
            .SingleOrDefaultAsync(cancellationToken);

        return rows is null ? new OrderTotals(0, 0m, 0m, 0) : new OrderTotals(rows.Count, rows.Collected, rows.Fees, rows.Waived);
    }

    public async Task<IReadOnlyList<Order>> ListForCustomerAsync(Guid customerUserId, int limit, CancellationToken cancellationToken) =>
        await db.Orders.AsNoTracking()
            .Where(x => x.CustomerUserId == customerUserId)
            .OrderByDescending(x => x.PlacedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public Task<bool> ReferenceExistsAsync(string reference, CancellationToken cancellationToken) =>
        db.Orders.AsNoTracking().AnyAsync(x => x.Reference == reference, cancellationToken);

    public async Task<IReadOnlyList<Order>> ListBalanceDueAsync(DateTimeOffset dueBefore, int max, CancellationToken cancellationToken) =>
        await db.Orders.TagWith(nameof(ListBalanceDueAsync))
            .Where(x => x.Status == OrderStatus.PartiallyPaid && x.BalanceDueAt <= dueBefore)
            .OrderBy(x => x.BalanceDueAt)
            .Take(max)
            .ToListAsync(cancellationToken);

    public void Add(Order order) => db.Orders.Add(order);
}

internal sealed class TicketRepository(TicketingDbContext db) : ITicketRepository
{
    public Task<bool> HoldsUsableTicketAsync(string phone, Guid eventId, CancellationToken cancellationToken) =>
        db.Tickets.AsNoTracking().TagWith(nameof(HoldsUsableTicketAsync)).AnyAsync(
            x => x.HolderPhone == phone && x.EventId == eventId &&
                 (x.Status == TicketStatus.Valid || x.Status == TicketStatus.CheckedIn),
            cancellationToken);

    public Task<bool> HoldsUsableTicketForDateAsync(string phone, Guid eventDateId, CancellationToken cancellationToken) =>
        db.Tickets.AsNoTracking().TagWith(nameof(HoldsUsableTicketForDateAsync)).AnyAsync(
            x => x.HolderPhone == phone && x.EventDateId == eventDateId &&
                 (x.Status == TicketStatus.Valid || x.Status == TicketStatus.CheckedIn),
            cancellationToken);

    public Task<Ticket?> FindAsync(TicketId id, CancellationToken cancellationToken) =>
        db.Tickets.TagWith(nameof(FindAsync)).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<Ticket?> FindForReadAsync(TicketId id, CancellationToken cancellationToken) =>
        db.Tickets.AsNoTracking().TagWith(nameof(FindForReadAsync)).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<Ticket?> FindByCodeAsync(string code, CancellationToken cancellationToken) =>
        db.Tickets.TagWith(nameof(FindByCodeAsync)).SingleOrDefaultAsync(x => x.Code == code, cancellationToken);

    public async Task<IReadOnlyList<Ticket>> SearchAsync(Guid eventDateId, string query, int limit, CancellationToken cancellationToken)
    {
        var tickets = db.Tickets.AsNoTracking().TagWith(nameof(SearchAsync)).Where(x => x.EventDateId == eventDateId);

        if (query.All(char.IsAsciiDigit))
        {
            // Last digits of the holder's phone.
            tickets = tickets.Where(x => x.HolderPhone.EndsWith(query));
        }
        else
        {
            var code = ShortCode.Normalize(query);
            var name = $"%{query.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal)}%";
            tickets = tickets.Where(x => x.Code.StartsWith(code) || EF.Functions.ILike(x.HolderName, name));
        }

        return await tickets.OrderBy(x => x.HolderName).Take(limit).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<TicketStatus, int>> CountByStatusAsync(Guid eventDateId, CancellationToken cancellationToken) =>
        await db.Tickets.AsNoTracking()
            .Where(x => x.EventDateId == eventDateId)
            .GroupBy(x => x.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);

    public async Task<IReadOnlyList<Ticket>> ListForHolderAsync(string phone, CancellationToken cancellationToken) =>
        await db.Tickets.AsNoTracking()
            .Where(x => x.HolderPhone == phone && x.Status != TicketStatus.Void)
            .OrderBy(x => x.DateStartsAt).ThenBy(x => x.IssuedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Ticket>> ListForOrderAsync(OrderId orderId, CancellationToken cancellationToken) =>
        await db.Tickets.Where(x => x.OrderId == orderId).OrderBy(x => x.IssuedAt).ToListAsync(cancellationToken);

    public Task<int> CountForOrderAsync(OrderId orderId, CancellationToken cancellationToken) =>
        db.Tickets.AsNoTracking().CountAsync(x => x.OrderId == orderId, cancellationToken);

    public async Task<IReadOnlyList<Ticket>> ListForEventAsync(
        Guid eventId,
        Guid? eventDateId,
        TicketStatus? status,
        string? query,
        DateTimeOffset? issuedBefore,
        int limit,
        CancellationToken cancellationToken)
    {
        var tickets = db.Tickets.AsNoTracking().TagWith("AdminListForEvent").Where(x => x.EventId == eventId);

        if (eventDateId is { } dateId)
        {
            tickets = tickets.Where(x => x.EventDateId == dateId);
        }

        if (status is { } wanted)
        {
            tickets = tickets.Where(x => x.Status == wanted);
        }

        if (issuedBefore is { } before)
        {
            tickets = tickets.Where(x => x.IssuedAt < before);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var text = query.Trim();
            if (text.All(char.IsAsciiDigit))
            {
                tickets = tickets.Where(x => x.HolderPhone.EndsWith(text));
            }
            else
            {
                var code = ShortCode.Normalize(text);
                var name = $"%{text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal)}%";
                tickets = tickets.Where(x =>
                    x.Code.StartsWith(code) ||
                    EF.Functions.ILike(x.HolderName, name) ||
                    (x.AttendeeName != null && EF.Functions.ILike(x.AttendeeName, name)));
            }
        }

        return await tickets.OrderByDescending(x => x.IssuedAt).Take(limit).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<TicketStatus, int>> CountByStatusForEventAsync(Guid eventId, CancellationToken cancellationToken) =>
        await db.Tickets.AsNoTracking()
            .Where(x => x.EventId == eventId)
            .GroupBy(x => x.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);

    public void AddRange(IEnumerable<Ticket> tickets) => db.Tickets.AddRange(tickets);
}

internal sealed class TicketingUnitOfWork(TicketingDbContext db, TimeProvider time) : ITicketingUnitOfWork
{
    public async Task<Result> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success;
        }
        catch (UniqueConstraintException ex)
            when (string.Equals(ex.ConstraintName, WaitlistEntryConfiguration.EventPhoneIndex, StringComparison.Ordinal))
        {
            // Lost a race with a concurrent join for the same phone: the other request already saved it.
            foreach (var entry in db.ChangeTracker.Entries<WaitlistEntry>().Where(x => x.State == EntityState.Added))
            {
                entry.State = EntityState.Detached;
            }

            return TicketingErrors.WaitlistAlreadyJoined;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            foreach (var entry in ex.Entries)
            {
                entry.State = EntityState.Detached;
            }

            return TicketingErrors.ConcurrentUpdate;
        }
    }

    public async Task<Result<T>> InTransactionAsync<T>(
        Func<CancellationToken, Task<Result<T>>> work,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var result = await work(cancellationToken);

            if (result.IsFailure)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                db.ChangeTracker.Clear();
                return result;
            }

            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    public Task<bool> AlreadyProcessedAsync(Guid messageId, string handler, CancellationToken cancellationToken) =>
        db.Set<ProcessedMessage>().AsNoTracking().AnyAsync(x => x.MessageId == messageId && x.Handler == handler, cancellationToken);

    public void MarkProcessed(Guid messageId, string handler) =>
        db.Set<ProcessedMessage>().Add(ProcessedMessage.For(messageId, handler, time.GetUtcNow()));
}
