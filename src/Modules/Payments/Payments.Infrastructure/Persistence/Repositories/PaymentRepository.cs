using Common.Domain.ResultMonad;
using Common.Infrastructure.Messaging;
using EntityFramework.Exceptions.Common;
using Payments.Infrastructure.Persistence.Configurations;
using Common.IntegrationEvents;
using Microsoft.EntityFrameworkCore;
using Payments.Application.Abstractions;
using Payments.Domain.Common;
using Payments.Domain.Destinations;
using Payments.Domain.Payments;

namespace Payments.Infrastructure.Persistence.Repositories;

internal sealed class PaymentRepository(PaymentsDbContext db) : IPaymentRepository
{
    public Task<Payment?> FindAsync(PaymentId id, CancellationToken cancellationToken) =>
        db.Payments.TagWith(nameof(FindAsync)).SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<Payment?> FindByCallbackTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        db.Payments.TagWith(nameof(FindByCallbackTokenHashAsync))
            .SingleOrDefaultAsync(x => x.CallbackTokenHash == tokenHash, cancellationToken);

    public async Task<IReadOnlyList<Payment>> ListDueForCheckAsync(
        DateTimeOffset createdBefore,
        DateTimeOffset checkedBefore,
        DateTimeOffset expiredAfter,
        int max,
        CancellationToken cancellationToken) =>
        await db.Payments
            .TagWith(nameof(ListDueForCheckAsync))
            .Where(x =>
                ((x.Status == PaymentStatus.Initiating || x.Status == PaymentStatus.Pending) && x.CreatedAt <= createdBefore) ||
                (x.Status == PaymentStatus.Expired && x.ProviderTransactionId != null && x.CreatedAt >= expiredAfter))
            .Where(x => x.LastCheckedAt == null || x.LastCheckedAt <= checkedBefore)
            .OrderBy(x => x.LastCheckedAt ?? x.CreatedAt)
            .Take(max)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Payment>> ListForReadAsync(
        PaymentStatus? status,
        Guid? referenceId,
        DateTimeOffset? before,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = db.Payments.AsNoTracking().TagWith(nameof(ListForReadAsync));

        if (status is { } s)
        {
            query = query.Where(x => x.Status == s);
        }

        if (referenceId is { } r)
        {
            query = query.Where(x => x.ReferenceId == r);
        }

        if (before is { } b)
        {
            query = query.Where(x => x.CreatedAt < b);
        }

        return await query.OrderByDescending(x => x.CreatedAt).Take(limit).ToListAsync(cancellationToken);
    }

    public async Task<PaymentTotals> SummarizeAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        var inPeriod = db.Payments.AsNoTracking().Where(x => x.CreatedAt >= from && x.CreatedAt < to);

        var byStatus = await inPeriod
            .GroupBy(x => x.Status)
            .Select(g => new { g.Key, Count = g.Count(), Amount = g.Sum(x => x.Amount) })
            .ToListAsync(cancellationToken);

        var settled = await inPeriod
            .Where(x => x.Status == PaymentStatus.Succeeded)
            .Join(
                db.Settlements.AsNoTracking().Where(s => s.Status == SettlementStatus.Settled),
                payment => payment.Id,
                settlement => settlement.Id,
                (payment, _) => payment.Amount)
            .ToListAsync(cancellationToken);

        return new PaymentTotals(
            [.. byStatus.OrderBy(x => x.Key).Select(x => new PaymentStatusTotal(x.Key, x.Count, x.Amount))],
            settled.Count,
            settled.Sum());
    }

    public void Add(Payment payment) => db.Payments.Add(payment);
}

internal sealed class SettlementRepository(PaymentsDbContext db) : ISettlementRepository
{
    public Task<SettlementRecord?> FindAsync(PaymentId paymentId, CancellationToken cancellationToken) =>
        db.Settlements.SingleOrDefaultAsync(x => x.Id == paymentId, cancellationToken);

    public async Task<IReadOnlyDictionary<PaymentId, SettlementRecord>> FindManyAsync(
        IReadOnlyCollection<PaymentId> paymentIds,
        CancellationToken cancellationToken)
    {
        if (paymentIds.Count == 0)
        {
            return new Dictionary<PaymentId, SettlementRecord>();
        }

        return await db.Settlements.AsNoTracking()
            .Where(x => paymentIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);
    }

    public void Add(SettlementRecord record) => db.Settlements.Add(record);
}

internal sealed class PaymentDestinationRepository(PaymentsDbContext db) : IPaymentDestinationRepository
{
    public Task<PaymentDestination?> FindAsync(PaymentDestinationId id, CancellationToken cancellationToken) =>
        db.Destinations.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<PaymentDestination?> FindActiveAsync(Guid kraftId, Guid eventId, CancellationToken cancellationToken) =>
        db.Destinations.SingleOrDefaultAsync(
            x => x.KraftId == kraftId && x.EventId == eventId &&
                 (x.Status == DestinationStatus.Submitted || x.Status == DestinationStatus.Approved),
            cancellationToken);

    public Task<PaymentDestination?> FindByTestPaymentAsync(Guid paymentId, CancellationToken cancellationToken) =>
        db.Destinations.SingleOrDefaultAsync(x => x.TestPaymentId == paymentId, cancellationToken);

    public async Task<IReadOnlyList<PaymentDestination>> ListForKraftAsync(Guid kraftId, CancellationToken cancellationToken) =>
        await db.Destinations.AsNoTracking()
            .Where(x => x.KraftId == kraftId)
            .OrderByDescending(x => x.SubmittedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<PaymentDestination>> ListAsync(
        DestinationStatus? status,
        Guid? eventId,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = db.Destinations.AsNoTracking();

        if (status is { } wanted)
        {
            query = query.Where(x => x.Status == wanted);
        }

        if (eventId is { } eventFilter)
        {
            query = query.Where(x => x.EventId == eventFilter);
        }

        return await query.OrderByDescending(x => x.SubmittedAt).Take(limit).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> KraftsWithApprovedTillAsync(Guid eventId, CancellationToken cancellationToken) =>
        (await db.Destinations.AsNoTracking()
            .Where(x => x.EventId == eventId && x.Status == DestinationStatus.Approved)
            .Select(x => x.KraftId)
            .ToListAsync(cancellationToken))
        .ToHashSet();

    public Task<PaymentDestination?> FindApprovedPlatformTillAsync(CancellationToken cancellationToken) =>
        db.Destinations.AsNoTracking()
            .Where(x => x.Kind == PaymentDestinationKind.PlatformTill && x.Status == DestinationStatus.Approved)
            .OrderByDescending(x => x.DecidedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<PaymentDestination>> ListPlatformTillsAsync(int limit, CancellationToken cancellationToken) =>
        await db.Destinations.AsNoTracking()
            .Where(x => x.Kind == PaymentDestinationKind.PlatformTill)
            .OrderByDescending(x => x.SubmittedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

    public void Add(PaymentDestination destination) => db.Destinations.Add(destination);
}

internal sealed class PaymentsUnitOfWork(PaymentsDbContext db, TimeProvider time) : IPaymentsUnitOfWork
{
    public void Publish(IntegrationEvent @event) => db.AddIntegrationEvent(@event, time.GetUtcNow());

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public async Task<Result> TrySaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return Result.Success;
        }
        catch (UniqueConstraintException ex)
            when (string.Equals(ex.ConstraintName, PaymentDestinationConfiguration.ActiveTillIndex, StringComparison.Ordinal))
        {
            Detach(db.ChangeTracker.Entries().Where(x => x.State == EntityState.Added));
            return PaymentsErrors.InvalidState("TillAlreadySubmitted", "This Kraft already has a till waiting for approval or approved for this event.");
        }
        catch (DbUpdateConcurrencyException ex)
        {
            Detach(ex.Entries);
            return PaymentsErrors.InvalidState("ConcurrentUpdate", "This was changed by someone else at the same moment. Try again.");
        }
    }

    public Task<bool> AlreadyProcessedAsync(Guid messageId, string handler, CancellationToken cancellationToken) =>
        db.Set<ProcessedMessage>().AsNoTracking().AnyAsync(x => x.MessageId == messageId && x.Handler == handler, cancellationToken);

    public void MarkProcessed(Guid messageId, string handler) =>
        db.Set<ProcessedMessage>().Add(ProcessedMessage.For(messageId, handler, time.GetUtcNow()));

    private static void Detach(IEnumerable<Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry> entries)
    {
        foreach (var entry in entries.ToList())
        {
            entry.State = EntityState.Detached;
        }
    }
}
