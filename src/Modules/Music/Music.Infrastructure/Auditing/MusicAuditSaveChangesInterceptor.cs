using Common.Application.Auditing;
using Common.Domain.Aggregates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Music.Infrastructure.Auditing;

/// <summary>
///     Projects Music domain events into <c>music.audit_log</c> in the same <c>SaveChanges</c> (and so the same
///     transaction) as the change that raised them (Spec 08 §13). Mirrors the Events and Identity (WP-08) interceptors: the event id
///     is the audit row id, events are cleared only after a successful save, and pending rows are detached when a
///     save fails so a retry re-projects them.
/// </summary>
public sealed class MusicAuditSaveChangesInterceptor(
    IAuditRequestContextProvider requestContext,
    TimeProvider time)
    : SaveChangesInterceptor
{
    private readonly List<MusicAuditRecord> _pendingRecords = [];
    private readonly List<IAggregateRoot> _pendingAggregates = [];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Capture(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Capture(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Complete();

        return base.SavedChanges(eventData, result);
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        Complete();

        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Rollback(eventData.Context);

        base.SaveChangesFailed(eventData);
    }

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Rollback(eventData.Context);

        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    public override void SaveChangesCanceled(DbContextEventData eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Rollback(eventData.Context);

        base.SaveChangesCanceled(eventData);
    }

    public override Task SaveChangesCanceledAsync(
        DbContextEventData eventData,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Rollback(eventData.Context);

        return base.SaveChangesCanceledAsync(eventData, cancellationToken);
    }

    private void Capture(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        Rollback(context);

        var tampered = context.ChangeTracker
            .Entries<MusicAuditRecord>()
            .Any(x => x.State is EntityState.Modified or EntityState.Deleted);

        if (tampered)
        {
            throw new InvalidOperationException("Music audit records are append-only.");
        }

        var aggregates = context.ChangeTracker
            .Entries<IAggregateRoot>()
            .Where(x => x.Entity.Events.Count > 0)
            .Select(x => x.Entity)
            .ToList();

        if (aggregates.Count == 0)
        {
            return;
        }

        var ambient = requestContext.GetCurrent();
        var now = time.GetUtcNow();

        foreach (var aggregate in aggregates)
        {
            foreach (var domainEvent in aggregate.Events)
            {
                var (action, target, metadata) = MusicAuditMapper.Map(domainEvent, aggregate);

                var record = MusicAuditRecord.From(new AuditEntry
                {
                    Id = domainEvent.Id,
                    OccurredAt = now,
                    Module = MusicAuditMapper.Module,
                    Action = action,
                    Outcome = AuditOutcome.Succeeded,
                    Actor = ambient.Actor,
                    TenantId = ambient.TenantId,
                    Target = target,
                    TraceId = ambient.TraceId,
                    RequestId = ambient.RequestId,
                    Metadata = metadata
                });

                context.Add(record);
                _pendingRecords.Add(record);
            }
        }

        _pendingAggregates.AddRange(aggregates);
    }

    private void Complete()
    {
        foreach (var aggregate in _pendingAggregates)
        {
            aggregate.ClearEvents();
        }

        _pendingAggregates.Clear();
        _pendingRecords.Clear();
    }

    private void Rollback(DbContext? context)
    {
        if (context is not null)
        {
            foreach (var record in _pendingRecords)
            {
                context.Entry(record).State = EntityState.Detached;
            }
        }

        _pendingAggregates.Clear();
        _pendingRecords.Clear();
    }
}
