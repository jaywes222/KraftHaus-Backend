namespace Common.Application.Auditing;

/// <summary>
///     The audit contract shared by every module (Spec 08 §13, WP-08.1).
///     <para>
///         An audit entry answers: who (<see cref="Actor" />) did what (<see cref="Action" />) to which
///         thing (<see cref="Target" />, <see cref="SubjectUserId" />), in which tenant, when, with what
///         <see cref="Outcome" />, and how to find the surrounding request (<see cref="TraceId" />,
///         <see cref="RequestId" />).
///     </para>
///     <para>
///         Entries are append-only and must never carry secrets or personal data. Free-form values are only
///         allowed in <see cref="Metadata" />, which is always passed through <see cref="AuditMetadataSanitizer" />
///         before it is persisted. Error descriptions are deliberately not part of the contract; only the stable
///         machine-readable error key is stored in <see cref="ReasonCode" />.
///     </para>
/// </summary>
public sealed record AuditEntry
{
    /// <summary>Unique id. For entries projected from a domain event this is the event id (idempotency key).</summary>
    public required Guid Id { get; init; }

    /// <summary>UTC time the audited action was recorded.</summary>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>Owning module, lower case (e.g. <c>identity</c>).</summary>
    public required string Module { get; init; }

    /// <summary>Stable dotted action name (e.g. <c>identity.session.revoked</c>). Never localised.</summary>
    public required string Action { get; init; }

    public required AuditOutcome Outcome { get; init; }

    /// <summary>The authenticated principal (or anonymous/system caller) that performed the action.</summary>
    public required AuditActor Actor { get; init; }

    /// <summary>The user whose security state the action concerns, when known. May differ from the actor.</summary>
    public Guid? SubjectUserId { get; init; }

    /// <summary>Tenant the action was performed in (resolved request tenant), when known.</summary>
    public Guid? TenantId { get; init; }

    public AuditTarget? Target { get; init; }

    /// <summary>Stable machine-readable reason for a failed/denied outcome (an <c>Error.Key</c>).</summary>
    public string? ReasonCode { get; init; }

    /// <summary>W3C trace id of the request/job that produced the entry (matches OpenTelemetry/Seq).</summary>
    public string? TraceId { get; init; }

    /// <summary>ASP.NET Core request identifier (<c>HttpContext.TraceIdentifier</c>), returned in problem details.</summary>
    public string? RequestId { get; init; }

    /// <summary>Small, non-sensitive key/value context. Sanitised before persistence.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}

public enum AuditOutcome
{
    /// <summary>The action completed.</summary>
    Succeeded = 1,

    /// <summary>The action was attempted and rejected because input/state was invalid.</summary>
    Failed = 2,

    /// <summary>The action was refused for a security reason (permission, lockout, token reuse).</summary>
    Denied = 3
}

public enum AuditActorType
{
    Anonymous = 1,
    User = 2,
    System = 3
}

/// <summary>Who performed an audited action. Ids only: never names, phones or e-mail addresses.</summary>
public sealed record AuditActor(
    AuditActorType Type,
    string? Id,
    string? SessionId)
{
    public static AuditActor Anonymous { get; } = new(AuditActorType.Anonymous, null, null);

    public static AuditActor System { get; } = new(AuditActorType.System, null, null);

    public static AuditActor User(
        string id,
        string? sessionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        return new AuditActor(AuditActorType.User, id, sessionId);
    }
}

/// <summary>The resource an audited action acted upon, identified by type and opaque id.</summary>
public sealed record AuditTarget(
    string Type,
    string Id);
