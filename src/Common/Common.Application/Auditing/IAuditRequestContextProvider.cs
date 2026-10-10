namespace Common.Application.Auditing;

/// <summary>
///     The ambient "who / where / which request" part of an audit entry, captured once per write.
/// </summary>
public sealed record AuditRequestContext(
    AuditActor Actor,
    Guid? TenantId,
    string? TraceId,
    string? RequestId)
{
    public static AuditRequestContext System(
        string? traceId) =>
        new(AuditActor.System, null, traceId, null);
}

/// <summary>
///     Resolves the actor, tenant and correlation ids for the current unit of work: the HTTP request when there
///     is one, otherwise the background job (actor = <see cref="AuditActorType.System" />).
/// </summary>
public interface IAuditRequestContextProvider
{
    AuditRequestContext GetCurrent();
}
