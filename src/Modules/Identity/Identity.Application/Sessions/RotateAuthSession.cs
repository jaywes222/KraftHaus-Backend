using System.Net;
using Common.Domain.ResultMonad;
using Common.Domain.TimeProvision;
using Identity.Application.Abstractions;
using Identity.Domain.Common;
using Identity.Domain.Sessions;

namespace Identity.Application.Sessions;

public sealed record RotateAuthSessionCommand(
    AuthSessionId SessionId,
    string RefreshToken);

public sealed record RotateAuthSessionResult(
    string RefreshToken,
    DateTimeOffset ExpiresAt,
    long Generation);

public sealed class RotateAuthSessionService(
    IAuthSessionRepository sessions,
    IRefreshTokenService refreshTokens,
    IIdentityUnitOfWork unitOfWork,
    TimeProvider time)
{
    public async Task<Result<RotateAuthSessionResult>> ExecuteAsync(
        RotateAuthSessionCommand command,
        CancellationToken cancellationToken)
    {
        var session = await sessions.FindAsync(
            command.SessionId,
            cancellationToken);

        if (session is null)
        {
            return Error.NotFound(
                nameof(command.SessionId),
                command.SessionId);
        }

        var now = time.GetUtcNow();

        var presentedSecretHash =
            refreshTokens.Hash(command.RefreshToken);

        var newRefreshToken =
            refreshTokens.Generate();

        var newSecretHash =
            refreshTokens.Hash(newRefreshToken);

        var previousGeneration =
            session.Generation;

        var rotateResult = session.Rotate(
            previousGeneration,
            presentedSecretHash,
            newSecretHash,
            now);

        if (rotateResult.IsFailure)
        {
            await sessions.SaveAsync(
                session,
                cancellationToken);

            await unitOfWork.SaveChangesAsync(
                cancellationToken);

            return rotateResult.Error!;
        }

        await sessions.SaveAsync(
            session,
            cancellationToken);

        await unitOfWork.SaveChangesAsync(
            cancellationToken);

        // If the previous-generation grace period was used,
        // Rotate() intentionally leaves the session generation
        // unchanged. Return the presented token rather than
        // creating another rotation from the same generation.
        return session.Generation == previousGeneration
            ? (Result<RotateAuthSessionResult>)new RotateAuthSessionResult(
                command.RefreshToken,
                session.ExpiresAt,
                session.Generation)
            : (Result<RotateAuthSessionResult>)new RotateAuthSessionResult(
            newRefreshToken,
            session.ExpiresAt,
            session.Generation);
    }
}
