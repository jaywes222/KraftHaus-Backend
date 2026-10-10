using Common.Application.Auditing;
using Common.Domain.ResultMonad;
using Common.Domain.StronglyTypedIds;
using Common.Domain.TimeProvision;
using Identity.Application.Abstractions;
using Identity.Application.Auditing;
using Identity.Domain.Common;
using Identity.Domain.Sessions;

namespace Identity.Application.Sessions;

/// <param name="SessionId">The session to revoke.</param>
/// <param name="UserId">The authenticated caller. Only the session's owner may revoke it.</param>
public sealed record RevokeAuthSessionCommand(
    AuthSessionId SessionId,
    ApplicationUserId UserId);

public sealed class RevokeAuthSessionService(
    IAuthSessionRepository sessions,
    IIdentityUnitOfWork unitOfWork,
    TimeProvider time,
    IIdentityAuditTrail auditTrail)
{
    public Task<Result> ExecuteAsync(
        RevokeAuthSessionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ExecuteCoreAsync(command, cancellationToken)
            .AuditRejectionAsync(
                auditTrail,
                error => new IdentityAuditRejection(
                    IdentityAuditActions.SessionRevokeRejected,
                    error)
                {
                    Target = new AuditTarget(
                        IdentityAuditTargets.AuthSession,
                        command.SessionId.Value.ToString())
                },
                cancellationToken);
    }

    private async Task<Result> ExecuteCoreAsync(
        RevokeAuthSessionCommand command,
        CancellationToken cancellationToken)
    {
        var session =
            await sessions.FindAsync(
                command.SessionId,
                cancellationToken);

        // A session owned by someone else is reported exactly like a missing one (no enumeration).
        if (session is null ||
            session.UserId != command.UserId)
        {
            return Error.NotFound(
                nameof(command.SessionId),
                command.SessionId);
        }

        var revokeResult =
            session.Revoke(
                SessionRevocationReason.SignedOut,
                time.GetUtcNow());

        if (revokeResult.IsFailure)
        {
            return revokeResult.Error!;
        }

        await sessions.SaveAsync(
            session,
            cancellationToken);

        await unitOfWork.SaveChangesAsync(
            cancellationToken);

        return Result.Success;
    }
}
