using Common.Domain.Aggregates;
using Identity.Application.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Identity.Infrastructure.Auditing;

/// <summary>
///     Projects Identity domain events into <c>identity.security_audit_log</c> inside the same
///     <c>SaveChanges</c> (and therefore the same database transaction) as the state change that raised them
///     (Spec 08 §13, WP-08.2). If the save fails, neither the change nor its audit rows are written.
///     <list type="bullet">
///         <item>Events are cleared from their aggregates after a successful save, so a second save in the same
///         scope (e.g. OTP proof followed by invitation acceptance) never audits an event twice.</item>
///         <item>On failure the pending audit rows are detached and the events kept, so a retry re-projects them
///         with the same ids (the event id is the audit row id).</item>
///         <item>Any attempt to modify or delete a tracked audit row fails the save before it reaches the database
///         (the database trigger is the second line of defence).</item>
///     </list>
/// </summary>
public sealed class IdentityAuditSaveChangesInterceptor(
    IdentityAuditEventMapper mapper,
    IdentityAuditEntryFactory entryFactory)
    : SaveChangesInterceptor
{
    private readonly List<IdentityAuditRecord> _pendingRecords = [];
    private readonly List<IAggregateRoot> _pendingAggregates = [];

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
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

    public override int SavedChanges(
        SaveChangesCompletedEventData eventData,
        int result)
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

        // A previous save on this context may have ended without a completion callback; start clean.
        Rollback(context);

        EnsureAppendOnly(context);

        var aggregates = context.ChangeTracker
            .Entries<IAggregateRoot>()
            .Where(x => x.Entity.Events.Count > 0)
            .Select(x => x.Entity)
            .ToList();

        var events = aggregates
            .SelectMany(aggregate => aggregate.Events.Select(@event => (@event, mapper.Map(@event, aggregate))))
            .ToList();

        foreach (var entry in entryFactory.FromEvents(events))
        {
            var record = IdentityAuditRecord.From(entry);

            context.Add(record);
            _pendingRecords.Add(record);
        }

        _pendingAggregates.AddRange(aggregates);
    }

    private static void EnsureAppendOnly(DbContext context)
    {
        var tampered = context.ChangeTracker
            .Entries<IdentityAuditRecord>()
            .Any(x => x.State is EntityState.Modified or EntityState.Deleted);

        if (tampered)
        {
            throw new InvalidOperationException(
                "Identity audit records are append-only and cannot be modified or deleted.");
        }
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
