using Common.Domain.ResultMonad;
using Common.Domain.StronglyTypedIds;
using Identity.Application.Abstractions;
using Identity.Application.Sessions;
using Identity.Domain.Common;
using Identity.Domain.Otp;
using Identity.Domain.Sessions;
using Identity.Domain.Users;

namespace Identity.Application.Auth;

public sealed record CompleteOtpLoginCommand(
    OtpChallengeId ChallengeId,
    string Code,
    string DeviceId);

public sealed record CompleteOtpLoginResult(
    ApplicationUserId UserId,
    AuthSessionId SessionId,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt);

public sealed class CompleteOtpLoginService(
    IOtpChallengeRepository challenges,
    IOtpCodeHasher otpHasher,
    IIdentityUserRepository users,
    IRefreshTokenService refreshTokens,
    IAuthSessionRepository sessions,
    ISessionTokenIssuer accessTokens,
    IIdentityUnitOfWork unitOfWork,
    TimeProvider time)
{
    public async Task<Result<CompleteOtpLoginResult>> ExecuteAsync(
        CompleteOtpLoginCommand command,
        CancellationToken cancellationToken)
    {
        var challenge =
            await challenges.FindAsync(
                command.ChallengeId,
                cancellationToken);

        if (challenge is null)
        {
            return Error.NotFound(
                nameof(command.ChallengeId),
                command.ChallengeId);
        }

        var codeHash =
            otpHasher.Hash(command.Code);

        var now = time.GetUtcNow();

        var verifyResult =
            challenge.Verify(
                codeHash,
                now);

        if (verifyResult.IsFailure)
        {
            await challenges.SaveAsync(
                challenge,
                cancellationToken);

            await unitOfWork.SaveChangesAsync(
                cancellationToken);

            return verifyResult.Error!;
        }

        var user =
            await FindUserAsync(
                challenge,
                cancellationToken);

        if (user is null)
        {
            return new Error
            {
                Key = "Identity.UserNotFound",
                Description =
                    "No application user exists for the verified identity."
            };
        }

        if (user.Status != UserStatus.Active)
        {
            return new Error
            {
                Key = "Identity.UserDisabled",
                Description =
                    "The application user is not active."
            };
        }

        var signInResult =
            user.RecordSignIn(now);

        if (signInResult.IsFailure)
        {
            return signInResult.Error!;
        }

        var refreshToken =
            refreshTokens.Generate();

        var refreshTokenHash =
            refreshTokens.Hash(refreshToken);

        var sessionResult =
            AuthSession.Start(
                new AuthSessionId(
                    Guid.CreateVersion7()),
                user.Id,
                command.DeviceId,
                refreshTokenHash,
                now);

        if (sessionResult.IsFailure)
        {
            return sessionResult.Error!;
        }

        var session = sessionResult.Value!;

        await challenges.SaveAsync(
            challenge,
            cancellationToken);

        await users.SaveAsync(
            user,
            cancellationToken);

        await sessions.AddAsync(
            session,
            cancellationToken);

        await unitOfWork.SaveChangesAsync(
            cancellationToken);

        var accessToken =
            await accessTokens.IssueAccessTokenAsync(
                user.Id,
                session.Id,
                cancellationToken);

        return new CompleteOtpLoginResult(
            user.Id,
            session.Id,
            accessToken,
            refreshToken,
            session.ExpiresAt);
    }

    private async Task<ApplicationUser?> FindUserAsync(
        OtpChallenge challenge,
        CancellationToken cancellationToken)
    {
        return challenge.Channel switch
        {
            OtpChannel.Phone =>
                await FindByPhoneAsync(
                    challenge.Destination,
                    cancellationToken),

            OtpChannel.Email =>
                await FindByEmailAsync(
                    challenge.Destination,
                    cancellationToken),

            _ => null
        };
    }

    private async Task<ApplicationUser?> FindByPhoneAsync(
        string value,
        CancellationToken cancellationToken)
    {
        var phone =
            PhoneNumber.Create(value);

        if (phone.IsFailure)
        {
            return null;
        }

        return await users.FindByPhoneAsync(
            phone.Value!,
            cancellationToken);
    }

    private async Task<ApplicationUser?> FindByEmailAsync(
        string value,
        CancellationToken cancellationToken)
    {
        var email =
            EmailAddress.Create(value);

        if (email.IsFailure)
        {
            return null;
        }

        return await users.FindByEmailAsync(
            email.Value!,
            cancellationToken);
    }
}
