using Common.Domain.ResultMonad;
using Common.Domain.StronglyTypedIds;
using Common.Domain.TimeProvision;
using Identity.Application.Abstractions;
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
    TimeProvider time)
{
    public async Task<Result<CreateAuthSessionResult>> ExecuteAsync(
        CreateAuthSessionCommand command,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();

        var refreshToken =
            refreshTokens.Generate();

        var refreshTokenHash =
            refreshTokens.Hash(refreshToken);

        var sessionResult = AuthSession.Start(
            new AuthSessionId(Guid.CreateVersion7()),
            command.UserId,
            command.DeviceId,
            refreshTokenHash,
            now);

        if (sessionResult.IsFailure)
        {
            return sessionResult.Error!;
        }

        var session = sessionResult.Value!;

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
