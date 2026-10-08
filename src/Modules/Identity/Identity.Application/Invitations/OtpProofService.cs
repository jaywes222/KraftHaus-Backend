using Identity.Application.Abstractions;
using Identity.Domain.Otp;

namespace Identity.Application.Invitations;

public sealed class OtpProofService(
    IOtpChallengeRepository challenges,
    IOtpCodeHasher codeHasher,
    IIdentityUnitOfWork unitOfWork,
    TimeProvider time)
    : IOtpProofService
{
    public async Task<bool> VerifyAsync(
        OtpChannel channel,
        string destination,
        string code,
        CancellationToken cancellationToken)
    {
        var challenge =
            await challenges.FindLatestPendingAsync(
                channel,
                destination,
                OtpPurpose.StaffInvitation,
                cancellationToken);

        if (challenge is null)
        {
            return false;
        }

        var suppliedHash =
            codeHasher.Hash(code);

        var result =
            challenge.Verify(
                suppliedHash,
                time.GetUtcNow());

        // Failed OTP verification can mutate the challenge by
        // incrementing attempts, locking, or expiring it.
        // Those mutations must be persisted.
        await challenges.SaveAsync(
            challenge,
            cancellationToken);

        await unitOfWork.SaveChangesAsync(
            cancellationToken);

        return !result.IsFailure;
    }
}
