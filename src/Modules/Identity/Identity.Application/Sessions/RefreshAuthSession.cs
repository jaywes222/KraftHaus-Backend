using Common.Application.Auditing;
using Common.Domain.ResultMonad;
using Common.Domain.TimeProvision;
using Identity.Application.Abstractions;
using Identity.Application.Auditing;
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
    TimeProvider time,
    IIdentityAuditTrail auditTrail)
{
    public Task<Result<RefreshAuthSessionResult>> ExecuteAsync(
        RefreshAuthSessionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ExecuteCoreAsync(command, cancellationToken)
            .AuditRejectionAsync(
                auditTrail,
                error => new IdentityAuditRejection(
                    IdentityAuditActions.SessionRefreshRejected,
                    error)
                {
                    Target = new AuditTarget(
                        IdentityAuditTargets.AuthSession,
                        command.SessionId.Value.ToString())
                },
                cancellationToken);
    }

    private async Task<Result<RefreshAuthSessionResult>> ExecuteCoreAsync(
        RefreshAuthSessionCommand command,
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
