using System.Security.Cryptography;
using System.Text;
using Common.Domain.Aggregates;
using Common.Domain.ResultMonad;
using Identity.Domain.Common;
using Identity.Domain.Events;

namespace Identity.Domain.Otp;

public sealed class OtpChallenge(OtpChallengeId id)
    : AggregateRoot<OtpChallengeId>(id)
{
    private const int CodeLength = 6;
    private const int MaxAttempts = 5;

    private static readonly TimeSpan _lifetime =
        TimeSpan.FromMinutes(5);

    private static readonly TimeSpan _resendCooldown =
        TimeSpan.FromSeconds(60);

    private OtpChallenge()
        : this(new OtpChallengeId(
            DefaultIdType.CreateVersion7()))
    {
    }

    public OtpChannel Channel { get; private set; }

    public string Destination { get; private set; } = string.Empty;

    public OtpPurpose Purpose { get; private set; }

    public OtpChallengeStatus Status { get; private set; }

    /// <summary>
    /// HMAC-derived hash of the OTP.
    /// The plaintext OTP is never persisted.
    /// </summary>
    public string CodeHash { get; private set; } = string.Empty;

    public DateTimeOffset IssuedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public int AttemptCount { get; private set; }

    public DateTimeOffset? VerifiedAt { get; private set; }

    public static Result<OtpChallenge> Issue(
        OtpChallengeId id,
        OtpChallenge? previous,
        OtpChannel channel,
        string destination,
        OtpPurpose purpose,
        string codeHash,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(destination))
        {
            return new Error
            {
                Key = "Identity.OtpDestinationRequired",
                Description = "OTP destination is required."
            };
        }

        if (string.IsNullOrWhiteSpace(codeHash))
        {
            return new Error
            {
                Key = "Identity.OtpCodeHashRequired",
                Description = "OTP code hash is required."
            };
        }

        if (previous is not null &&
            previous.Status == OtpChallengeStatus.Pending)
        {
            if (now < previous.IssuedAt + _resendCooldown)
            {
                return new Error
                {
                    Key = "Identity.OtpResendTooSoon",
                    Description = "A new OTP cannot be requested yet."
                };
            }

            previous.Supersede();
        }

        var challenge = new OtpChallenge(id)
        {
            Channel = channel,
            Destination = destination.Trim(),
            Purpose = purpose,
            CodeHash = codeHash,
            IssuedAt = now,
            ExpiresAt = now + _lifetime,
            Status = OtpChallengeStatus.Pending,
            AttemptCount = 0
        };

        challenge.RaiseEvent(
            new OtpIssued(
                challenge.Id,
                challenge.Channel,
                challenge.Destination,
                challenge.Purpose,
                challenge.ExpiresAt));

        return challenge;
    }

    public Result Verify(
        string suppliedCodeHash,
        DateTimeOffset now)
    {
        if (Status == OtpChallengeStatus.Locked)
        {
            return new Error
            {
                Key = "Identity.OtpLocked",
                Description =
                    "The OTP challenge is locked."
            };
        }

        if (Status != OtpChallengeStatus.Pending)
        {
            return new Error
            {
                Key = "Identity.OtpNotPending",
                Description = "The OTP challenge is no longer pending."
            };
        }

        if (now >= ExpiresAt)
        {
            Status = OtpChallengeStatus.Expired;

            RaiseEvent(new OtpExpired(Id));

            return new Error
            {
                Key = "Identity.OtpExpired",
                Description = "The OTP has expired."
            };
        }

        AttemptCount++;

        var expected =
            Encoding.UTF8.GetBytes(CodeHash);

        var supplied =
            Encoding.UTF8.GetBytes(
                suppliedCodeHash ?? string.Empty);

        var matches =
            expected.Length == supplied.Length &&
            CryptographicOperations.FixedTimeEquals(
                expected,
                supplied);

        if (!matches)
        {
            RaiseEvent(
                new OtpAttemptFailed(
                    Id,
                    AttemptCount));

            if (AttemptCount >= MaxAttempts)
            {
                Status = OtpChallengeStatus.Locked;

                RaiseEvent(new OtpLocked(Id));

                return new Error
                {
                    Key = "Identity.OtpLocked",
                    Description =
                        "The OTP challenge is locked."
                };
            }

            return new Error
            {
                Key = "Identity.OtpInvalid",
                Description = "The OTP is invalid."
            };
        }

        Status = OtpChallengeStatus.Verified;
        VerifiedAt = now;

        RaiseEvent(new OtpVerified(Id));

        return Result.Success;
    }

    private void Supersede()
    {
        if (Status != OtpChallengeStatus.Pending)
        {
            return;
        }

        Status = OtpChallengeStatus.Superseded;

        RaiseEvent(new OtpSuperseded(Id));
    }

    public bool IsExpired(DateTimeOffset now) =>
        Status == OtpChallengeStatus.Pending &&
        now >= ExpiresAt;

    public static int RequiredCodeLength =>
        CodeLength;

    public static int MaximumAttempts =>
        MaxAttempts;

    public static TimeSpan ChallengeLifetime =>
        _lifetime;

    public static TimeSpan ResendDelay =>
        _resendCooldown;
}
