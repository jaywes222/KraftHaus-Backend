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
        if (!refreshTokens.TryParse(
                command.RefreshToken,
                out var token))
        {
            return new Error
            {
                Key = "Identity.InvalidRefreshToken",
                Description =
                    "The refresh token is invalid.",
                StatusCode =
                    System.Net.HttpStatusCode.Unauthorized
            };
        }

        if (token.SessionId != command.SessionId)
        {
            return new Error
            {
                Key = "Identity.RefreshTokenSessionMismatch",
                Description =
                    "The refresh token does not belong to the requested session.",
                StatusCode =
                    System.Net.HttpStatusCode.Unauthorized
            };
        }

        var session =
            await sessions.FindAsync(
                token.SessionId,
                cancellationToken);

        if (session is null)
        {
            return Error.NotFound(
                nameof(command.SessionId),
                command.SessionId);
        }

        var now =
            time.GetUtcNow();

        var newSecret =
            refreshTokens.GenerateSecret();

        var newSecretHash =
            refreshTokens.HashSecret(
                newSecret);

        var rotateResult =
            session.Rotate(
                token.Generation,
                refreshTokens.HashSecret(
                    token.Secret),
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

        var returnedRefreshToken =
            rotateResult.Value ==
            AuthSessionRotationOutcome.PreviousGenerationGrace
                ? command.RefreshToken
                : refreshTokens.Create(
                    session.Id,
                    session.Generation,
                    newSecret);

        return new RotateAuthSessionResult(
            returnedRefreshToken,
            session.ExpiresAt,
            session.Generation);
    }
}
