using Common.Application.Auditing;
using Common.Application.Tenancy;
using Common.Domain.ResultMonad;
using Common.Domain.StronglyTypedIds;
using Common.Domain.Tenancy;
using Identity.Application.Abstractions;
using Identity.Application.Auditing;
using Identity.Application.Options;
using Identity.Application.Sessions;
using Identity.Domain.Common;
using Identity.Domain.Otp;
using Identity.Domain.Sessions;
using Identity.Domain.Users;
using Microsoft.Extensions.Options;

namespace Identity.Application.Auth;

public sealed record CompleteOtpLoginCommand(
    OtpChallengeId ChallengeId,
    string Code,
    string DeviceId,
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
    TimeProvider time,
    IIdentityAuditTrail auditTrail,
    ICurrentTenant currentTenant)
{
    private readonly IdentityPolicyOptions _policies =
        policyOptions.Value;

    public Task<Result<CompleteOtpLoginResult>> ExecuteAsync(
        CompleteOtpLoginCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ExecuteCoreAsync(command, cancellationToken)
            .AuditRejectionAsync(
                auditTrail,
                error => new IdentityAuditRejection(
                    IdentityAuditActions.SignInRejected,
                    error)
                {
                    Target = new AuditTarget(
                        IdentityAuditTargets.OtpChallenge,
                        command.ChallengeId.Value.ToString())
                },
                cancellationToken);
    }

    private async Task<Result<CompleteOtpLoginResult>> ExecuteCoreAsync(
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

        /*
         * The identity realm (scope + tenant) comes from the tenant resolved for this host, never from the
         * request body: an unauthenticated caller must not choose which directory it registers into.
         */
        if (!currentTenant.IsResolved)
        {
            return IdentityErrors.InvalidState(
                "TenantUnresolved",
                "Sign-in requires a tenant host.");
        }

        var realm =
            IdentityRealm.ForHost(
                currentTenant.TenantId,
                currentTenant.IdentityScope);

        var challenge =
            await challenges.FindAsync(
                command.ChallengeId,
                cancellationToken);

        // A code issued on another tenant's host is treated as unknown here (no cross-tenant sign-in, no oracle).
        if (challenge is null || !challenge.BelongsTo(currentTenant.TenantId))
        {
            return Error.NotFound(
                nameof(command.ChallengeId),
                command.ChallengeId);
        }

        var now =
            time.GetUtcNow();

        var codeHash =
            otpHasher.Hash(
                command.Code);

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
                realm,
                challenge,
                cancellationToken);

        var registered =
            false;

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
                            realm,
                            userId,
                            challenge,
                            command,
                            termsResult.Value!,
                            privacyResult.Value!,
                            now),

                    OtpChannel.Email =>
                        RegisterEmailUser(
                            realm,
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

            user =
                registrationResult.Value!;

            await users.AddAsync(
                user,
                cancellationToken);

            registered =
                true;
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

        /*
         * Refresh-token secret is generated independently from
         * the public token metadata.
         *
         * Only the secret hash is persisted in AuthSession.
         */
        var refreshSecret =
            refreshTokens.GenerateSecret();

        var refreshSecretHash =
            refreshTokens.HashSecret(
                refreshSecret);

        var sessionId =
            new AuthSessionId(
                Guid.CreateVersion7());

        var sessionResult =
            AuthSession.Start(
                sessionId,
                user.Id,
                command.DeviceId,
                refreshSecretHash,
                now);

        if (sessionResult.IsFailure)
        {
            return sessionResult.Error!;
        }

        var session =
            sessionResult.Value!;

        /*
         * The public refresh token contains:
         *
         * session id
         * generation
         * secret
         *
         * AuthSession stores only the secret hash.
         */
        var refreshToken =
            refreshTokens.Create(
                session.Id,
                session.Generation,
                refreshSecret);

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
        IdentityRealm realm,
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
            realm.Scope,
            realm.TenantId,
            phoneResult.Value!,
            command.DisplayName,
            terms,
            privacy,
            now);
    }

    private static Result<ApplicationUser> RegisterEmailUser(
        IdentityRealm realm,
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
            realm.Scope,
            realm.TenantId,
            emailResult.Value!,
            command.DisplayName,
            terms,
            privacy,
            now);
    }

    private async Task<ApplicationUser?> FindUserAsync(
        IdentityRealm realm,
        OtpChallenge challenge,
        CancellationToken cancellationToken)
    {
        return challenge.Channel switch
        {
            OtpChannel.Phone =>
                await FindByPhoneAsync(
                    realm,
                    challenge.Destination,
                    cancellationToken),

            OtpChannel.Email =>
                await FindByEmailAsync(
                    realm,
                    challenge.Destination,
                    cancellationToken),

            _ => null
        };
    }

    private async Task<ApplicationUser?> FindByPhoneAsync(
        IdentityRealm realm,
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
            realm,
            phone.Value!,
            cancellationToken);
    }

    private async Task<ApplicationUser?> FindByEmailAsync(
        IdentityRealm realm,
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
            realm,
            email.Value!,
            cancellationToken);
    }
}
