using Common.Application.Auditing;
using Common.Domain.ResultMonad;

namespace Identity.Application.Auditing;

/// <summary>
///     Records refused Identity requests (WP-08.2). Successful state changes do not go through here: they are
///     projected from domain events inside the same database transaction as the change.
///     <para>
///         Implementations write independently of the unit of work, so recording a rejection never flushes
///         half-finished aggregate changes (for example an OTP challenge that verified but whose sign-in was then
///         refused). Recording is best effort: a failure to write is logged and never replaces the original error.
///     </para>
/// </summary>
public interface IIdentityAuditTrail
{
    Task RecordRejectedAsync(
        IdentityAuditRejection rejection,
        CancellationToken cancellationToken);
}

/// <summary>
///     A refused request. Only <see cref="Error.Key" /> is persisted from <see cref="Error" />; descriptions and
///     values are dropped because they can echo user input.
/// </summary>
public sealed record IdentityAuditRejection(
    string Action,
    Error Error)
{
    public AuditTarget? Target { get; init; }

    public Guid? SubjectUserId { get; init; }

    public IReadOnlyDictionary<string, string>? Metadata { get; init; }

    /// <summary>
    ///     Optional phone number / e-mail address the request targeted. It is never stored: the trail replaces it
    ///     with a keyed <c>destinationFingerprint</c> so abuse against one destination can still be correlated.
    /// </summary>
    public string? DestinationToFingerprint { get; init; }
}
