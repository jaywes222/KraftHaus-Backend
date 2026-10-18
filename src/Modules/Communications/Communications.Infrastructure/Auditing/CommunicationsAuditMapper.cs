using System.Globalization;
using Common.Application.Auditing;
using Common.Domain.Aggregates;
using Common.Domain.Events;
using Communications.Domain.Broadcasts;
using Communications.Domain.OptOuts;
using Communications.Domain.Templates;

namespace Communications.Infrastructure.Auditing;

/// <summary>
///     Communications domain events → audit actions (<c>communications.broadcast.*</c>, <c>communications.optout.*</c>,
///     <c>communications.template.*</c>). No phone numbers or message text.
/// </summary>
public static class CommunicationsAuditMapper
{
    public const string Module = "communications";

    public static (string Action, AuditTarget Target, IReadOnlyDictionary<string, string> Metadata) Map(
        DomainEvent domainEvent,
        IAggregateRoot aggregate)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        ArgumentNullException.ThrowIfNull(aggregate);

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);

        var (action, targetType) = domainEvent switch
        {
            BroadcastCreated created => (Broadcast("communications.broadcast.created", created.EventId, created.Audience.ToString()), "broadcast"),
            BroadcastUpdated updated => (Broadcast("communications.broadcast.updated", updated.EventId, updated.Audience.ToString()), "broadcast"),
            BroadcastScheduled scheduled => (Scheduled(scheduled), "broadcast"),
            BroadcastCancelled cancelled => (Broadcast("communications.broadcast.cancelled", cancelled.EventId, null), "broadcast"),
            BroadcastStarted started => (Started(started), "broadcast"),
            BroadcastCompleted completed => (Completed(completed), "broadcast"),
            PhoneOptedOut optedOut => (Source("communications.optout.added", optedOut.Source), "opt_out"),
            PhoneOptedIn optedIn => (Source("communications.optout.removed", optedIn.Source), "opt_out"),
            MessageTemplateSaved => ("communications.template.saved", "message_template"),
            MessageTemplateArchived => ("communications.template.archived", "message_template"),
            _ => throw new ArgumentOutOfRangeException(
                nameof(domainEvent),
                domainEvent.GetType().Name,
                "Every Communications domain event needs a stable audit action.")
        };

        return (action, new AuditTarget(targetType, aggregate.Id.Value.ToString("D", CultureInfo.InvariantCulture)), metadata);

        string Broadcast(string name, Guid eventId, string? audience)
        {
            metadata["eventId"] = Id(eventId);
            if (audience is not null)
            {
                metadata["audience"] = audience;
            }

            return name;
        }

        string Scheduled(BroadcastScheduled scheduled)
        {
            metadata["eventId"] = Id(scheduled.EventId);
            metadata["sendAt"] = scheduled.SendAt.ToString("O", CultureInfo.InvariantCulture);
            return "communications.broadcast.scheduled";
        }

        string Started(BroadcastStarted started)
        {
            metadata["eventId"] = Id(started.EventId);
            metadata["recipients"] = started.Recipients.ToString(CultureInfo.InvariantCulture);
            metadata["skipped"] = started.Skipped.ToString(CultureInfo.InvariantCulture);
            return "communications.broadcast.started";
        }

        string Completed(BroadcastCompleted completed)
        {
            metadata["eventId"] = Id(completed.EventId);
            metadata["sent"] = completed.Sent.ToString(CultureInfo.InvariantCulture);
            metadata["failed"] = completed.Failed.ToString(CultureInfo.InvariantCulture);
            return "communications.broadcast.completed";
        }

        string Source(string name, OptOutSource source)
        {
            metadata["source"] = source.ToString();
            return name;
        }
    }

    private static string Id(Guid id) => id.ToString("D", CultureInfo.InvariantCulture);
}
