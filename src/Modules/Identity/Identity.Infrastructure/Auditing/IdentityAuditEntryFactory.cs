using System.Net;
using Common.Application.Auditing;
using Common.Domain.Events;
using Identity.Application.Auditing;

namespace Identity.Infrastructure.Auditing;

/// <summary>
///     Combines what happened (a domain-event descriptor or a rejection) with the ambient request context
///     (actor, tenant, trace/request ids) and the clock into a complete <see cref="AuditEntry" />.
/// </summary>
public sealed class IdentityAuditEntryFactory(
    IAuditRequestContextProvider requestContext,
    IAuditFingerprinter fingerprinter,
    TimeProvider time)
{
    public AuditEntry FromEvent(
        DomainEvent @event,
        IdentityAuditDescriptor descriptor) =>
        FromEvents([(@event, descriptor)])[0];

    /// <summary>
    ///     Builds the entries for one <c>SaveChanges</c>: they share the request context and the same timestamp,
    ///     which makes "everything this transaction changed" trivially queryable.
    /// </summary>
    public IReadOnlyList<AuditEntry> FromEvents(
        IReadOnlyCollection<(DomainEvent Event, IdentityAuditDescriptor Descriptor)> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        if (events.Count == 0)
        {
            return [];
        }

        var context = requestContext.GetCurrent();
        var now = time.GetUtcNow();

        return events
            .Select(x => FromEvent(x.Event, x.Descriptor, context, now))
            .ToList();
    }

    private static AuditEntry FromEvent(
        DomainEvent @event,
        IdentityAuditDescriptor descriptor,
        AuditRequestContext context,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentNullException.ThrowIfNull(descriptor);

        return new AuditEntry
        {
            // The event id doubles as the idempotency key: the same event can never be audited twice.
            Id = @event.Id,
            OccurredAt = now,
            Module = IdentityAuditActions.Module,
            Action = descriptor.Action,
            Outcome = descriptor.Outcome,
            Actor = context.Actor,
            SubjectUserId = descriptor.SubjectUserId,
            TenantId = context.TenantId,
            Target = descriptor.Target,
            ReasonCode = descriptor.ReasonCode,
            TraceId = context.TraceId,
            RequestId = context.RequestId,
            Metadata = descriptor.Metadata
        };
    }

    public AuditEntry FromRejection(IdentityAuditRejection rejection)
    {
        ArgumentNullException.ThrowIfNull(rejection);

        var context = requestContext.GetCurrent();
        var now = time.GetUtcNow();

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);

        if (rejection.Metadata is not null)
        {
            foreach (var (key, value) in rejection.Metadata)
            {
                metadata[key] = value;
            }
        }

        if (!string.IsNullOrWhiteSpace(rejection.DestinationToFingerprint))
        {
            metadata["destinationFingerprint"] =
                fingerprinter.Fingerprint(rejection.DestinationToFingerprint);
        }

        return new AuditEntry
        {
            Id = Guid.CreateVersion7(now),
            OccurredAt = now,
            Module = IdentityAuditActions.Module,
            Action = rejection.Action,
            Outcome = OutcomeFor(rejection.Error.StatusCode),
            Actor = context.Actor,
            SubjectUserId = rejection.SubjectUserId,
            TenantId = context.TenantId,
            Target = rejection.Target,

            // Only the stable key: descriptions and values can echo user input (codes, phone numbers, names).
            ReasonCode = rejection.Error.Key,
            TraceId = context.TraceId,
            RequestId = context.RequestId,
            Metadata = metadata
        };
    }

    private static AuditOutcome OutcomeFor(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            ? AuditOutcome.Denied
            : AuditOutcome.Failed;
}
