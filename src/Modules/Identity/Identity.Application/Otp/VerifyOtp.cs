using System.Diagnostics.Metrics;
using System.Runtime.Intrinsics.X86;
using Common.Domain.ResultMonad;
using Common.Domain.StronglyTypedIds;
using Identity.Application.Abstractions;
using Identity.Domain.Common;
using Identity.Domain.Otp;
using Identity.Domain.Users;

namespace Identity.Application.Otp;

public sealed record VerifyOtpCommand(
    OtpChallengeId ChallengeId,
    string Code);

public sealed record VerifyOtpResult(
    ApplicationUserId? UserId,
    bool IsNewUser);

public sealed class VerifyOtpService(
    IOtpChallengeRepository challenges,
    IOtpCodeHasher codeHasher,
    IIdentityUserRepository users,
    IIdentityUnitOfWork unitOfWork,
    TimeProvider time)
{
    public async Task<Result<VerifyOtpResult>> ExecuteAsync(
        VerifyOtpCommand command,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.Code) ||
            command.Code.Length != OtpChallenge.RequiredCodeLength ||
            !command.Code.All(char.IsDigit))
        {
            return IdentityErrors.Invalid(
                nameof(command.Code),
                "OTP code must contain exactly six digits.");
        }

        var challenge = await challenges.FindAsync(
            command.ChallengeId,
            cancellationToken);

        if (challenge is null)
        {
            return Error.NotFound(
                nameof(command.ChallengeId),
                command.ChallengeId);
        }

        var suppliedHash = codeHasher.Hash(command.Code);

        var now = time.GetUtcNow();

        var verifyResult = challenge.Verify(
            suppliedHash,
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

        await challenges.SaveAsync(
            challenge,
            cancellationToken);

        await unitOfWork.SaveChangesAsync(
            cancellationToken);

        var user = await FindUserAsync(
            challenge,
            cancellationToken);

        if (user is null)
        {
            return new VerifyOtpResult(
                null,
                true);
        }

        var signInResult = user.RecordSignIn(now);

        if (signInResult.IsFailure)
        {
            return signInResult.Error!;
        }

        await users.SaveAsync(
            user,
            cancellationToken);

        await unitOfWork.SaveChangesAsync(
            cancellationToken);

        return new VerifyOtpResult(
            user.Id,
            false);
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
        string destination,
        CancellationToken cancellationToken)
    {
        var result = PhoneNumber.Create(destination);

        return result.IsFailure
            ? null
            : await users.FindByPhoneAsync(
                result.Value!,
                cancellationToken);
    }

    private async Task<ApplicationUser?> FindByEmailAsync(
        string destination,
        CancellationToken cancellationToken)
    {
        var result = EmailAddress.Create(destination);

        return result.IsFailure
            ? null
            : await users.FindByEmailAsync(
                result.Value!,
                cancellationToken);
    }
}
