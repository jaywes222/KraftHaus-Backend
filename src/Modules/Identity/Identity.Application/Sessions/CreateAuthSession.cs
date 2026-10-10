using Common.Application.Auditing;
using Common.Domain.ResultMonad;
using Common.Domain.StronglyTypedIds;
using Common.Domain.TimeProvision;
using Identity.Application.Abstractions;
using Identity.Application.Auditing;
using Identity.Domain.Common;
using Identity.Domain.Sessions;

namespace Identity.Application.Sessions;

public sealed record CreateAuthSessionCommand(
    ApplicationUserId UserId,
    string DeviceId);

public sealed record CreateAuthSessionResult(
    AuthSessionId SessionId,
    string RefreshToken,
    DateTimeOffset ExpiresAt);

public sealed class CreateAuthSessionService(
    IRefreshTokenService refreshTokens,
    IAuthSessionRepository sessions,
    IIdentityUnitOfWork unitOfWork,
    TimeProvider time,
    IIdentityAuditTrail auditTrail)
{
    public Task<Result<CreateAuthSessionResult>> ExecuteAsync(
        CreateAuthSessionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ExecuteCoreAsync(command, cancellationToken)
            .AuditRejectionAsync(
                auditTrail,
                error => new IdentityAuditRejection(
                    IdentityAuditActions.SessionStartRejected,
                    error)
                {
                    SubjectUserId = command.UserId.Value,
                    Target = new AuditTarget(
                        IdentityAuditTargets.User,
                        command.UserId.Value.ToString())
                },
                cancellationToken);
    }

    private async Task<Result<CreateAuthSessionResult>> ExecuteCoreAsync(
        CreateAuthSessionCommand command,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();

        var sessionId =
            new AuthSessionId(
                Guid.CreateVersion7());

        var secret =
            refreshTokens.GenerateSecret();

        var secretHash =
            refreshTokens.HashSecret(secret);

        var sessionResult =
            AuthSession.Start(
                sessionId,
                command.UserId,
                command.DeviceId,
                secretHash,
                now);

        if (sessionResult.IsFailure)
        {
            return sessionResult.Error!;
        }

        var session =
            sessionResult.Value!;

        var refreshToken =
            refreshTokens.Create(
                session.Id,
                session.Generation,
                secret);

        await sessions.AddAsync(
            session,
            cancellationToken);

        await unitOfWork.SaveChangesAsync(
            cancellationToken);

        return new CreateAuthSessionResult(
            session.Id,
            refreshToken,
            session.ExpiresAt);
    }
}
