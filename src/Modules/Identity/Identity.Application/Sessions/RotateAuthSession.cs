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

        /*
         * Generate a candidate replacement token.
         *
         * Rotate() will only use its hash when the
         * presented token is the current generation.
         */
        var newRefreshToken =
            refreshTokens.Generate();

        var newSecretHash =
            refreshTokens.Hash(newRefreshToken);

        var rotateResult = session.Rotate(
            session.Generation,
            presentedSecretHash,
            newSecretHash,
            now);

        if (rotateResult.IsFailure)
        {
            // Important:
            // Rotate() may revoke the session when reuse
            // is detected, so persist the domain change.
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

        return rotateResult.Value ==
            AuthSessionRotationOutcome.PreviousGenerationGrace
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
