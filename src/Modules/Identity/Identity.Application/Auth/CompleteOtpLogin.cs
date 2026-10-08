using Common.Domain.ResultMonad;
using Common.Domain.StronglyTypedIds;
using Identity.Application.Abstractions;
using Identity.Application.Options;
using Identity.Application.Sessions;
using Identity.Domain.Common;
using Identity.Domain.Otp;
using Identity.Domain.Sessions;
using Identity.Domain.Users;
using Microsoft.Extensions.Options;
using Tenancy.Domain.Common;
using Tenancy.Domain.Tenants;

namespace Identity.Application.Auth;

public sealed record CompleteOtpLoginCommand(
    OtpChallengeId ChallengeId,
    string Code,
    string DeviceId,
    IdentityScope Scope,
    TenantId? TenantId,
    string DisplayName,
    bool AcceptTerms,
    bool AcceptPrivacy);

public sealed record CompleteOtpLoginResult(
    ApplicationUserId UserId,
    AuthSessionId SessionId,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    bool Registered);

public sealed class CompleteOtpLoginService(
    IOtpChallengeRepository challenges,
    IOtpCodeHasher otpHasher,
    IIdentityUserRepository users,
    IRefreshTokenService refreshTokens,
    IAuthSessionRepository sessions,
    ISessionTokenIssuer accessTokens,
    IIdentityUnitOfWork unitOfWork,
    IOptions<IdentityPolicyOptions> policyOptions,
    TimeProvider time)
{
    private readonly IdentityPolicyOptions _policies =
        policyOptions.Value;

    public async Task<Result<CompleteOtpLoginResult>> ExecuteAsync(
        CompleteOtpLoginCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Code))
        {
            return IdentityErrors.Required(
                nameof(command.Code),
                "Verification code is required.");
        }

        if (string.IsNullOrWhiteSpace(command.DeviceId))
        {
            return IdentityErrors.Required(
                nameof(command.DeviceId),
                "Device id is required.");
        }

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

        var now = time.GetUtcNow();

        var codeHash =
            otpHasher.Hash(command.Code);

        var verifyResult =
            challenge.Verify(
                codeHash,
                now);

        /*
         * This is deliberately persisted even when verification fails.
         *
         * The domain mutates:
         * - attempts
         * - expiry/status
         * - lockout state
         *
         * Therefore a failed verification is still a state change.
         */
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

        var registered = false;

        if (user is null)
        {
            if (!command.AcceptTerms)
            {
                return IdentityErrors.Required(
                    nameof(command.AcceptTerms),
                    "Terms acceptance is required when creating an account.");
            }

            if (!command.AcceptPrivacy)
            {
                return IdentityErrors.Required(
                    nameof(command.AcceptPrivacy),
                    "Privacy acceptance is required when creating an account.");
            }

            var termsResult =
                PolicyRef.Create(
                    _policies.Terms.Key,
                    _policies.Terms.Version,
                    _policies.Terms.TextHash);

            if (termsResult.IsFailure)
            {
                return termsResult.Error!;
            }

            var privacyResult =
                PolicyRef.Create(
                    _policies.Privacy.Key,
                    _policies.Privacy.Version,
                    _policies.Privacy.TextHash);

            if (privacyResult.IsFailure)
            {
                return privacyResult.Error!;
            }

            var userId =
                ApplicationUserId.New();

            var registrationResult =
                challenge.Channel switch
                {
                    OtpChannel.Phone =>
                        RegisterPhoneUser(
                            userId,
                            challenge,
                            command,
                            termsResult.Value!,
                            privacyResult.Value!,
                            now),

                    OtpChannel.Email =>
                        RegisterEmailUser(
                            userId,
                            challenge,
                            command,
                            termsResult.Value!,
                            privacyResult.Value!,
                            now),

                    _ =>
                        Result<ApplicationUser>.Failure(
                            IdentityErrors.Invalid(
                                nameof(challenge.Channel),
                                "Unsupported OTP channel."))
                };

            if (registrationResult.IsFailure)
            {
                return registrationResult.Error!;
            }

            user = registrationResult.Value!;

            await users.AddAsync(
                user,
                cancellationToken);

            registered = true;
        }
        else
        {
            if (user.Status != UserStatus.Active)
            {
                return new Error
                {
                    Key = "Identity.UserDisabled",
                    Description =
                        "The application user is not active."
                };
            }

            /*
             * Existing users do not get their scope or tenant
             * rewritten from an unauthenticated request.
             */
            var signInResult =
                user.RecordSignIn(now);

            if (signInResult.IsFailure)
            {
                return signInResult.Error!;
            }

            await users.SaveAsync(
                user,
                cancellationToken);
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

        var session =
            sessionResult.Value!;

        await challenges.SaveAsync(
            challenge,
            cancellationToken);

        await sessions.AddAsync(
            session,
            cancellationToken);

        await unitOfWork.SaveChangesAsync(
            cancellationToken);

        /*
         * Access tokens are issued only after the user/session
         * transaction has committed.
         */
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
            session.ExpiresAt,
            registered);
    }

    private static Result<ApplicationUser> RegisterPhoneUser(
        ApplicationUserId userId,
        OtpChallenge challenge,
        CompleteOtpLoginCommand command,
        PolicyRef terms,
        PolicyRef privacy,
        DateTimeOffset now)
    {
        var phoneResult =
            PhoneNumber.Create(
                challenge.Destination);

        if (phoneResult.IsFailure)
        {
            return phoneResult.Error!;
        }

        return ApplicationUser.RegisterVerifiedPhone(
            userId,
            command.Scope,
            command.TenantId,
            phoneResult.Value!,
            command.DisplayName,
            terms,
            privacy,
            now);
    }

    private static Result<ApplicationUser> RegisterEmailUser(
        ApplicationUserId userId,
        OtpChallenge challenge,
        CompleteOtpLoginCommand command,
        PolicyRef terms,
        PolicyRef privacy,
        DateTimeOffset now)
    {
        var emailResult =
            EmailAddress.Create(
                challenge.Destination);

        if (emailResult.IsFailure)
        {
            return emailResult.Error!;
        }

        return ApplicationUser.RegisterVerifiedEmail(
            userId,
            command.Scope,
            command.TenantId,
            emailResult.Value!,
            command.DisplayName,
            terms,
            privacy,
            now);
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
