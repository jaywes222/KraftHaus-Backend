using Common.Domain.ResultMonad;
using Common.Domain.TimeProvision;
using Identity.Application.Abstractions;
using Identity.Domain.Common;
using Identity.Domain.Sessions;

namespace Identity.Application.Sessions;

public sealed record RefreshAuthSessionCommand(
    AuthSessionId SessionId,
    string RefreshToken);

public sealed record RefreshAuthSessionResult(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt);

public sealed class RefreshAuthSessionService(
    IAuthSessionRepository sessions,
    IRefreshTokenService refreshTokens,
    ISessionTokenIssuer accessTokens,
    IIdentityUnitOfWork unitOfWork,
    TimeProvider time)
{
    public async Task<Result<RefreshAuthSessionResult>> ExecuteAsync(
        RefreshAuthSessionCommand command,
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

        var now = time.GetUtcNow();

        var presentedSecretHash =
            refreshTokens.Hash(command.RefreshToken);

        var newRefreshToken =
            refreshTokens.Generate();

        var newSecretHash =
            refreshTokens.Hash(newRefreshToken);

        var previousGeneration =
            session.Generation;

        var rotateResult =
            session.Rotate(
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

        /*
         * AuthSession.Rotate() accepts the previous generation
         * during the short previous-token grace period.
         *
         * In that case the domain does not advance Generation,
         * so do not issue another token from the same generation.
         */
        var returnedRefreshToken =
            session.Generation == previousGeneration
                ? command.RefreshToken
                : newRefreshToken;

        var accessToken =
            await accessTokens.IssueAccessTokenAsync(
                session.UserId,
                session.Id,
                cancellationToken);

        return new RefreshAuthSessionResult(
            accessToken,
            returnedRefreshToken,
            session.ExpiresAt);
    }
}
