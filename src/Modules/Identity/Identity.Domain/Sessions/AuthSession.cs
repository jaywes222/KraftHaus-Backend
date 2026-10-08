using System.Security.Cryptography;
using System.Text;
using Common.Domain.Aggregates;
using Common.Domain.ResultMonad;
using Common.Domain.StronglyTypedIds;
using Identity.Domain.Common;
using Identity.Domain.Events;

namespace Identity.Domain.Sessions;

public sealed class AuthSession(AuthSessionId id)
    : AggregateRoot<AuthSessionId>(id)
{
    public static readonly TimeSpan SlidingLifetime =
        TimeSpan.FromDays(30);

    public static readonly TimeSpan MaximumLifetime =
        TimeSpan.FromDays(90);

    public static readonly TimeSpan PreviousGenerationGrace =
        TimeSpan.FromSeconds(10);

    private AuthSession()
        : this(new AuthSessionId(
            Guid.CreateVersion7()))
    {
    }

    public ApplicationUserId UserId { get; private set; }

    public string DeviceId { get; private set; } =
        string.Empty;

    public AuthSessionStatus Status { get; private set; }

    public long Generation { get; private set; }

    public string CurrentSecretHash { get; private set; } =
        string.Empty;

    public long? PreviousGeneration { get; private set; }

    public string? PreviousSecretHash { get; private set; }

    public DateTimeOffset? PreviousRotatedAt { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset AbsoluteExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public SessionRevocationReason? RevocationReason { get; private set; }

    public static Result<AuthSession> Start(
        AuthSessionId id,
        ApplicationUserId userId,
        string deviceId,
        string secretHash,
        DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            return new Error
            {
                Key = "Identity.SessionDeviceRequired",
                Description = "Device id is required."
            };
        }

        if (string.IsNullOrWhiteSpace(secretHash))
        {
            return new Error
            {
                Key = "Identity.SessionSecretHashRequired",
                Description =
                    "Session secret hash is required."
            };
        }

        var session = new AuthSession(id)
        {
            UserId = userId,
            DeviceId = deviceId.Trim(),
            Status = AuthSessionStatus.Active,
            Generation = 1,
            CurrentSecretHash = secretHash,
            StartedAt = now,
            AbsoluteExpiresAt = now + MaximumLifetime,
            ExpiresAt = Min(
                now + SlidingLifetime,
                now + MaximumLifetime)
        };

        session.RaiseEvent(
            new SessionStarted(
                session.Id,
                session.UserId));

        return session;
    }

    public Result<AuthSessionRotationOutcome> Rotate(
        long presentedGeneration,
        string presentedSecretHash,
        string newSecretHash,
        DateTimeOffset now)
    {
        if (Status != AuthSessionStatus.Active)
        {
            return new Error
            {
                Key = "Identity.SessionNotActive",
                Description =
                    "The session is not active."
            };
        }

        if (now >= ExpiresAt ||
            now >= AbsoluteExpiresAt)
        {
            return new Error
            {
                Key = "Identity.SessionExpired",
                Description =
                    "The session has expired."
            };
        }

        if (string.IsNullOrWhiteSpace(newSecretHash))
        {
            return new Error
            {
                Key = "Identity.SessionSecretHashRequired",
                Description =
                    "New session secret hash is required."
            };
        }

        if (Matches(
                presentedGeneration,
                presentedSecretHash,
                Generation,
                CurrentSecretHash))
        {
            PreviousGeneration = Generation;
            PreviousSecretHash = CurrentSecretHash;
            PreviousRotatedAt = now;

            Generation++;

            CurrentSecretHash = newSecretHash;

            ExpiresAt = Min(
                now + SlidingLifetime,
                AbsoluteExpiresAt);

            RaiseEvent(
                new SessionTokenRotated(
                    Id,
                    Generation));

            return AuthSessionRotationOutcome.Rotated;
        }

        if (PreviousGeneration.HasValue &&
            PreviousSecretHash is not null &&
            PreviousRotatedAt.HasValue &&
            now <=
                PreviousRotatedAt.Value +
                PreviousGenerationGrace &&
            Matches(
                presentedGeneration,
                presentedSecretHash,
                PreviousGeneration.Value,
                PreviousSecretHash))
        {
            return AuthSessionRotationOutcome.PreviousGenerationGrace;
        }

        Revoke(
            SessionRevocationReason.ReuseDetected,
            now);

        return new Error
        {
            Key = "Identity.SessionReuseDetected",
            Description =
                "An old refresh token was presented and the session has been revoked.",
            StatusCode =
                System.Net.HttpStatusCode.Unauthorized
        };
    }

    public Result Revoke(
        SessionRevocationReason reason,
        DateTimeOffset now)
    {
        if (Status == AuthSessionStatus.Revoked)
        {
            return Result.Success;
        }

        Status = AuthSessionStatus.Revoked;
        RevokedAt = now;
        RevocationReason = reason;

        RaiseEvent(
            new SessionRevoked(
                Id,
                reason));

        return Result.Success;
    }

    private static bool Matches(
        long generation,
        string supplied,
        long expectedGeneration,
        string expectedHash)
    {
        if (generation != expectedGeneration)
        {
            return false;
        }

        var left =
            Encoding.UTF8.GetBytes(expectedHash);

        var right =
            Encoding.UTF8.GetBytes(
                supplied ?? string.Empty);

        return left.Length == right.Length &&
               CryptographicOperations.FixedTimeEquals(
                   left,
                   right);
    }

    private static DateTimeOffset Min(
        DateTimeOffset a,
        DateTimeOffset b) =>
        a <= b ? a : b;
}
