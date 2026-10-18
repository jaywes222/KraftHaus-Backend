using Commerce.Application.Abstractions;
using Commerce.Application.Settlements;
using Commerce.Domain.Settlements;
using Commerce.Domain.Common;
using Commerce.Domain.Krafts;
using Commerce.Domain.Menus;
using Commerce.Domain.Orders;
using Common.Domain.ResultMonad;
using Common.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Commerce.Infrastructure.Persistence.Repositories;

internal sealed class KraftRepository(CommerceDbContext db) : IKraftRepository
{
    public Task<Kraft?> FindAsync(KraftId id, CancellationToken cancellationToken) =>
        db.Krafts.AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<Kraft?> FindForReadAsync(KraftId id, CancellationToken cancellationToken) =>
        db.Krafts.AsNoTracking().AsSplitQuery().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken) =>
        db.Krafts.AnyAsync(x => x.Slug == slug, cancellationToken);

    public async Task<IReadOnlyList<Kraft>> ListForReadAsync(KraftStatus? status, Guid? eventId, CancellationToken cancellationToken)
    {
        var query = db.Krafts.AsNoTracking().AsSplitQuery();

        if (status is { } wanted)
        {
            query = query.Where(x => x.Status == wanted);
        }

        if (eventId is { } eventFilter)
        {
            query = query.Where(x => x.Participations.Any(p => p.EventId == eventFilter));
        }

        return await query.OrderBy(x => x.Name).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Kraft>> ListByIdsForReadAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var keys = ids.Select(x => new KraftId(x)).ToList();

        return await db.Krafts.AsNoTracking().AsSplitQuery().Where(x => keys.Contains(x.Id)).ToListAsync(cancellationToken);
    }

    public void Add(Kraft kraft) => db.Krafts.Add(kraft);
}

internal sealed class MenuItemRepository(CommerceDbContext db) : IMenuItemRepository
{
    public Task<MenuItem?> FindAsync(MenuItemId id, CancellationToken cancellationToken) =>
        db.MenuItems.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<MenuItem>> ListForKraftAsync(Guid kraftId, Guid? eventId, bool includeRetired, CancellationToken cancellationToken)
    {
        var query = db.MenuItems.AsNoTracking().Where(x => x.KraftId == kraftId);

        if (eventId is { } eventFilter)
        {
            query = query.Where(x => x.EventId == eventFilter);
        }

        if (!includeRetired)
        {
            query = query.Where(x => x.Status == MenuItemStatus.Active);
        }

        return await query.OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MenuItem>> ListActiveForEventAsync(Guid eventId, CancellationToken cancellationToken) =>
        await db.MenuItems.AsNoTracking()
            .Where(x => x.EventId == eventId && x.Status == MenuItemStatus.Active)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<MenuItem>> ListByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        var keys = ids.Select(x => new MenuItemId(x)).ToList();

        return await db.MenuItems.AsNoTracking().Where(x => keys.Contains(x.Id)).ToListAsync(cancellationToken);
    }

    public void Add(MenuItem item) => db.MenuItems.Add(item);
}

internal sealed class KraftOrderRepository(CommerceDbContext db) : IKraftOrderRepository
{
    public Task<KraftOrder?> FindAsync(KraftOrderId id, CancellationToken cancellationToken) =>
        db.Orders.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<KraftOrder?> FindForReadAsync(KraftOrderId id, CancellationToken cancellationToken) =>
        db.Orders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<KraftOrder>> ListByIdempotencyKeyAsync(Guid customerUserId, string key, CancellationToken cancellationToken) =>
        await db.Orders.Where(x => x.CustomerUserId == customerUserId && x.IdempotencyKey == key).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<KraftOrder>> ListGroupAsync(Guid checkoutGroupId, CancellationToken cancellationToken) =>
        await db.Orders.AsNoTracking().Where(x => x.CheckoutGroupId == checkoutGroupId).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<KraftOrder>> ListForCustomerAsync(Guid customerUserId, int limit, CancellationToken cancellationToken) =>
        await db.Orders.AsNoTracking()
            .Where(x => x.CustomerUserId == customerUserId)
            .OrderByDescending(x => x.PlacedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<KraftOrder>> ListHoldExpiredAsync(DateTimeOffset now, int max, CancellationToken cancellationToken) =>
        await db.Orders
            .Where(x => x.Status == KraftOrderStatus.Ordered && x.HoldExpiresAt <= now)
            .OrderBy(x => x.HoldExpiresAt)
            .Take(max)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<KraftOrder>> ListBoardAsync(Guid kraftId, Guid? eventId, DateTimeOffset collectedSince, CancellationToken cancellationToken)
    {
        var query = db.Orders.AsNoTracking().Where(x =>
            x.KraftId == kraftId &&
            (x.Status == KraftOrderStatus.Paid || x.Status == KraftOrderStatus.Preparing || x.Status == KraftOrderStatus.Ready ||
             (x.Status == KraftOrderStatus.Collected && x.CollectedAt >= collectedSince)));

        if (eventId is { } eventFilter)
        {
            query = query.Where(x => x.EventId == eventFilter);
        }

        return await query.OrderBy(x => x.PaidAt).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<KraftOrder>> ListReadyForEventAsync(
        Guid eventId,
        IReadOnlyCollection<Guid> kraftIds,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = db.Orders.AsNoTracking().Where(x => x.EventId == eventId && x.Status == KraftOrderStatus.Ready);

        if (kraftIds.Count > 0)
        {
            var keys = kraftIds.ToList();
            query = query.Where(x => keys.Contains(x.KraftId));
        }

        return await query.OrderBy(x => x.ReadyAt).Take(limit).ToListAsync(cancellationToken);
    }

    public Task<KraftOrder?> FindByPickupCodeAsync(Guid kraftId, string code, CancellationToken cancellationToken) =>
        db.Orders.SingleOrDefaultAsync(x => x.KraftId == kraftId && x.PickupCode == code, cancellationToken);

    public async Task<IReadOnlyList<KraftOrder>> ListUncollectedAsync(DateTimeOffset readyBefore, int max, CancellationToken cancellationToken) =>
        await db.Orders
            .Where(x => x.Status == KraftOrderStatus.Ready && x.ReadyAt <= readyBefore && x.UncollectedFlaggedAt == null)
            .OrderBy(x => x.ReadyAt)
            .Take(max)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<KraftOrder>> ListForAdminAsync(
        Guid? eventId,
        Guid? kraftId,
        KraftOrderStatus? status,
        bool needsActionOnly,
        DateTimeOffset? placedBefore,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = db.Orders.AsNoTracking();

        if (eventId is { } eventFilter)
        {
            query = query.Where(x => x.EventId == eventFilter);
        }

        if (kraftId is { } kraftFilter)
        {
            query = query.Where(x => x.KraftId == kraftFilter);
        }

        if (status is { } wanted)
        {
            query = query.Where(x => x.Status == wanted);
        }

        if (needsActionOnly)
        {
            query = query.Where(x => x.AttentionReason != null && x.AttentionResolvedAt == null);
        }

        if (placedBefore is { } before)
        {
            query = query.Where(x => x.PlacedAt < before);
        }

        return await query.OrderByDescending(x => x.PlacedAt).Take(limit).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<KraftOrder>> ListPaidAsync(Guid eventId, Guid? kraftId, CancellationToken cancellationToken)
    {
        var query = db.Orders.AsNoTracking().Where(x =>
            x.EventId == eventId &&
            (x.Status == KraftOrderStatus.Paid || x.Status == KraftOrderStatus.Preparing ||
             x.Status == KraftOrderStatus.Ready || x.Status == KraftOrderStatus.Collected));

        if (kraftId is { } kraftFilter)
        {
            query = query.Where(x => x.KraftId == kraftFilter);
        }

        return await query.ToListAsync(cancellationToken);
    }

    public Task<bool> ReferenceExistsAsync(string reference, CancellationToken cancellationToken) =>
        db.Orders.AnyAsync(x => x.Reference == reference, cancellationToken);

    public void AddRange(IEnumerable<KraftOrder> orders) => db.Orders.AddRange(orders);
}

internal sealed class CommerceUnitOfWork(CommerceDbContext db, TimeProvider time) : ICommerceUnitOfWork
{
    public async Task<Result> SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success;
        }
        catch (DbUpdateConcurrencyException ex)
        {
            foreach (var entry in ex.Entries)
            {
                entry.State = EntityState.Detached;
            }

            return CommerceErrors.ConcurrentUpdate;
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

internal sealed class KraftSettlementRepository(CommerceDbContext db) : IKraftSettlementRepository
{
    public async Task<IReadOnlyList<KraftSettlement>> ListForEventAsync(Guid eventId, Guid? kraftId, CancellationToken cancellationToken) =>
        await db.Settlements.AsNoTracking()
            .Where(x => x.EventId == eventId && (kraftId == null || x.KraftId == kraftId))
            .OrderBy(x => x.PaidAt)
            .ToListAsync(cancellationToken);

    public void Add(KraftSettlement settlement) => db.Settlements.Add(settlement);
}
