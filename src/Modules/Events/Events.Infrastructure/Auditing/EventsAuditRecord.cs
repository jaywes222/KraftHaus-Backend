using Common.Application.Auditing;

namespace Events.Infrastructure.Auditing;

/// <summary>
///     Persistence model for <c>events.audit_log</c> in each tenant database: who changed which event or venue,
///     when, in which request. Same shape as Identity's security audit log (Spec 08 §13, WP-08).
///     <para>
///         Not an <c>AuditableEntity</c>: an audit row is immutable, append-only plumbing. The database rejects
///         updates and deletes through a trigger.
///     </para>
/// </summary>
public sealed class EventsAuditRecord
{
    public const string TableName = "audit_log";

    public const int ModuleMaxLength = 32;
    public const int ActionMaxLength = 64;
    public const int EnumMaxLength = 16;
    public const int ActorIdMaxLength = 64;
    public const int TargetTypeMaxLength = 64;
    public const int TargetIdMaxLength = 128;
    public const int TraceIdMaxLength = 64;
    public const int RequestIdMaxLength = 128;

    private EventsAuditRecord()
    {
    }

    public Guid Id { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public string Module { get; private set; } = string.Empty;

    public string Action { get; private set; } = string.Empty;

    public string Outcome { get; private set; } = string.Empty;

    public string ActorType { get; private set; } = string.Empty;

    public string? ActorId { get; private set; }

    public string? ActorSessionId { get; private set; }

    public Guid? TenantId { get; private set; }

    public string? TargetType { get; private set; }

    public string? TargetId { get; private set; }

    public string? TraceId { get; private set; }

    public string? RequestId { get; private set; }

    public IReadOnlyDictionary<string, string> Metadata { get; private set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public static EventsAuditRecord From(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Module);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Action);

        return new EventsAuditRecord
        {
            Id = entry.Id,
            OccurredAt = entry.OccurredAt.ToUniversalTime(),
            Module = Fit(entry.Module, ModuleMaxLength)!,
            Action = Fit(entry.Action, ActionMaxLength)!,
            Outcome = entry.Outcome.ToString(),
            ActorType = entry.Actor.Type.ToString(),
            ActorId = Fit(entry.Actor.Id, ActorIdMaxLength),
            ActorSessionId = Fit(entry.Actor.SessionId, ActorIdMaxLength),
            TenantId = entry.TenantId,
            TargetType = Fit(entry.Target?.Type, TargetTypeMaxLength),
            TargetId = Fit(entry.Target?.Id, TargetIdMaxLength),
            TraceId = Fit(entry.TraceId, TraceIdMaxLength),
            RequestId = Fit(entry.RequestId, RequestIdMaxLength),
            Metadata = AuditMetadataSanitizer.Sanitize(entry.Metadata)
        };
    }

    private static string? Fit(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();

        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
