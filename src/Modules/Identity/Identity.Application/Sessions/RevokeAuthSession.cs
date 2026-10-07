using Common.Domain.ResultMonad;
using Common.Domain.TimeProvision;
using Identity.Application.Abstractions;
using Identity.Domain.Common;
using Identity.Domain.Sessions;

namespace Identity.Application.Sessions;

public sealed record RevokeAuthSessionCommand(
    AuthSessionId SessionId);

public sealed class RevokeAuthSessionService(
    IAuthSessionRepository sessions,
    IIdentityUnitOfWork unitOfWork,
    TimeProvider time)
{
    public async Task<Result> ExecuteAsync(
        RevokeAuthSessionCommand command,
        CancellationToken cancellationToken)
    {
        var session =
            await sessions.FindAsync(
                command.SessionId,
                cancellationToken);

        if (session is null)
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
