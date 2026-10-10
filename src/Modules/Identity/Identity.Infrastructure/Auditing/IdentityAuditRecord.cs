using Common.Application.Auditing;

namespace Identity.Infrastructure.Auditing;

/// <summary>
///     Persistence model for <c>identity.security_audit_log</c> (WP-08.2).
///     <para>
///         Deliberately not an <c>AuditableEntity</c>/<c>AggregateRoot</c>: an audit row is immutable, append-only
///         infrastructure plumbing (the same sanctioned exception as <c>OutboxMessage</c>). <c>CreatedOn</c>/
///         <c>LastModifiedBy</c> would be meaningless, and the database rejects updates and deletes through a trigger.
///     </para>
///     <para>
///         <see cref="From" /> is the single choke point every audit row passes through, so metadata sanitising and
///         column-length truncation cannot be bypassed by either capture path.
///     </para>
/// </summary>
public sealed class IdentityAuditRecord
{
    public const string TableName = "security_audit_log";
    public const string Schema = "identity";

    public const int ModuleMaxLength = 32;
    public const int ActionMaxLength = 64;
    public const int EnumMaxLength = 16;
    public const int ActorIdMaxLength = 64;
    public const int TargetTypeMaxLength = 64;
    public const int TargetIdMaxLength = 128;
    public const int ReasonCodeMaxLength = 128;
    public const int TraceIdMaxLength = 64;
    public const int RequestIdMaxLength = 128;

    private IdentityAuditRecord()
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

    public Guid? SubjectUserId { get; private set; }

    public Guid? TenantId { get; private set; }

    public string? TargetType { get; private set; }

    public string? TargetId { get; private set; }

    public string? ReasonCode { get; private set; }

    public string? TraceId { get; private set; }

    public string? RequestId { get; private set; }

    public IReadOnlyDictionary<string, string> Metadata { get; private set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public static IdentityAuditRecord From(AuditEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Module);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Action);

        return new IdentityAuditRecord
        {
            Id = entry.Id,
            OccurredAt = entry.OccurredAt.ToUniversalTime(),
            Module = Fit(entry.Module, ModuleMaxLength)!,
            Action = Fit(entry.Action, ActionMaxLength)!,
            Outcome = entry.Outcome.ToString(),
            ActorType = entry.Actor.Type.ToString(),
            ActorId = Fit(entry.Actor.Id, ActorIdMaxLength),
            ActorSessionId = Fit(entry.Actor.SessionId, ActorIdMaxLength),
            SubjectUserId = entry.SubjectUserId,
            TenantId = entry.TenantId,
            TargetType = Fit(entry.Target?.Type, TargetTypeMaxLength),
            TargetId = Fit(entry.Target?.Id, TargetIdMaxLength),
            ReasonCode = Fit(entry.ReasonCode, ReasonCodeMaxLength),
            TraceId = Fit(entry.TraceId, TraceIdMaxLength),
            RequestId = Fit(entry.RequestId, RequestIdMaxLength),
            Metadata = AuditMetadataSanitizer.Sanitize(entry.Metadata)
        };
    }

    private static string? Fit(
        string? value,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();

        return trimmed.Length <= maxLength
            ? trimmed
            : trimmed[..maxLength];
    }
}
