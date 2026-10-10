using Common.Application.Tenancy;
using Common.Domain.ResultMonad;
using Identity.Application.Auditing;
using Common.Domain.TimeProvision;
using Identity.Application.Abstractions;
using Identity.Domain.Common;
using Identity.Domain.Otp;
using Identity.Domain.Users;

namespace Identity.Application.Otp;

public sealed record RequestOtpCommand(
    OtpChannel Channel,
    string Destination,
    OtpPurpose Purpose);

public sealed record RequestOtpResult(
    OtpChallengeId ChallengeId,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RetryAfter);

public sealed class RequestOtpService(
    IOtpChallengeRepository challenges,
    IOtpCodeGenerator codeGenerator,
    IOtpCodeHasher codeHasher,
    IOtpSender sender,
    IIdentityUnitOfWork unitOfWork,
    TimeProvider time,
    IIdentityAuditTrail auditTrail,
    ICurrentTenant currentTenant)
{
    public Task<Result<RequestOtpResult>> ExecuteAsync(
        RequestOtpCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ExecuteCoreAsync(command, cancellationToken)
            .AuditRejectionAsync(
                auditTrail,
                error => new IdentityAuditRejection(
                    IdentityAuditActions.OtpRequestRejected,
                    error)
                {
                    DestinationToFingerprint = command.Destination,
                    Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["channel"] = command.Channel.ToString(),
                        ["purpose"] = command.Purpose.ToString()
                    }
                },
                cancellationToken);
    }

    private async Task<Result<RequestOtpResult>> ExecuteCoreAsync(
        RequestOtpCommand command,
        CancellationToken cancellationToken)
    {
        var destinationResult = NormalizeDestination(
            command.Channel,
            command.Destination);

        if (destinationResult.IsFailure)
        {
            return destinationResult.Error!;
        }

        var destination = destinationResult.Value!;

        // The challenge is bound to the host's tenant: it can only be redeemed on that tenant's hosts.
        if (!currentTenant.IsResolved)
        {
            return IdentityErrors.InvalidState(
                "TenantUnresolved",
                "Requesting a code requires a tenant host.");
        }

        var previous = await challenges.FindLatestPendingAsync(
            currentTenant.TenantId,
            command.Channel,
            destination,
            command.Purpose,
            cancellationToken);

        var now = time.GetUtcNow();

        var code = codeGenerator.Generate();
        var hash = codeHasher.Hash(code);

        var challengeResult = OtpChallenge.Issue(
            new OtpChallengeId(Guid.CreateVersion7()),
            previous,
            command.Channel,
            destination,
            command.Purpose,
            hash,
            now,
            currentTenant.TenantId);

        if (challengeResult.IsFailure)
        {
            return challengeResult.Error!;
        }

        var challenge = challengeResult.Value!;

        await challenges.AddAsync(
            challenge,
            cancellationToken);

        await unitOfWork.SaveChangesAsync(
            cancellationToken);

        await sender.SendAsync(
            command.Channel,
            destination,
            code,
            command.Purpose,
            cancellationToken);

        return new RequestOtpResult(
            challenge.Id,
            challenge.ExpiresAt,
            time.FromNow(OtpChallenge.ResendDelay));
    }

    private static Result<string> NormalizeDestination(
        OtpChannel channel,
        string destination)
    {
        switch (channel)
        {
            case OtpChannel.Phone:
                {
                    var result = PhoneNumber.Create(destination);

                    return result.IsFailure
                        ? result.Error!
                        : result.Value!.Value;
                }

            case OtpChannel.Email:
                {
                    var result = EmailAddress.Create(destination);

                    return result.IsFailure
                        ? result.Error!
                        : result.Value!.Value;
                }

            default:
                return IdentityErrors.Invalid(
                    nameof(channel),
                    "Unsupported OTP channel.");
        }
    }
}
