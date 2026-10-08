using System.Security.Policy;
using Identity.Domain.Common;
using Identity.Domain.Sessions;
using Xunit;

namespace Identity.Tests;

public sealed class AuthSessionTests
{
    private readonly TestTimeProvider _timeProvider =
        new(
            new DateTimeOffset(
                2026,
                10,
                8,
                5,
                0,
                0,
                TimeSpan.Zero));

    [Fact]
    public void StartUsesThirtyDaySlidingAndNinetyDayAbsoluteLifetime()
    {
        var now = _timeProvider.GetUtcNow();

        var result = AuthSession.Start(
            new AuthSessionId(Guid.CreateVersion7()),
            new Common.Domain.StronglyTypedIds.ApplicationUserId(
                Guid.CreateVersion7()),
            "device-1",
            "hash-1",
            now);

        Assert.False(result.IsFailure);

        var session = result.Value;

        Assert.NotNull(session);

        Assert.Equal(
            AuthSessionStatus.Active,
            session.Status);

        Assert.Equal(
            1,
            session.Generation);

        Assert.Equal(
            now.AddDays(30),
            session.ExpiresAt);

        Assert.Equal(
            now.AddDays(90),
            session.AbsoluteExpiresAt);
    }

    [Fact]
    public void RotateAdvancesGenerationAndStoresPreviousGeneration()
    {
        var session = CreateSession();

        _timeProvider.Advance(
            TimeSpan.FromMinutes(1));

        var result = session.Rotate(
            1,
            "hash-1",
            "hash-2",
            _timeProvider.GetUtcNow());

        Assert.False(result.IsFailure);

        Assert.Equal(
            AuthSessionRotationOutcome.Rotated,
            result.Value);

        Assert.Equal(2, session.Generation);
        Assert.Equal(1, session.PreviousGeneration);
        Assert.Equal("hash-1", session.PreviousSecretHash);
        Assert.Equal("hash-2", session.CurrentSecretHash);
    }

    [Fact]
    public void RotateAllowsPreviousGenerationOnlyDuringGraceWindow()
    {
        var session = CreateSession();

        _timeProvider.Advance(
            TimeSpan.FromMinutes(1));

        var firstRotation = session.Rotate(
            1,
            "hash-1",
            "hash-2",
            _timeProvider.GetUtcNow());

        Assert.False(firstRotation.IsFailure);

        _timeProvider.Advance(
            TimeSpan.FromSeconds(5));

        var result = session.Rotate(
            1,
            "hash-1",
            "hash-ignored",
            _timeProvider.GetUtcNow());

        Assert.False(result.IsFailure);

        Assert.Equal(
            AuthSessionRotationOutcome.PreviousGenerationGrace,
            result.Value);

        Assert.Equal(
            2,
            session.Generation);

        Assert.Equal(
            "hash-2",
            session.CurrentSecretHash);
    }

    [Fact]
    public void RotateRevokesSessionWhenOldTokenIsReusedOutsideGraceWindow()
    {
        var session = CreateSession();

        _timeProvider.Advance(
            TimeSpan.FromMinutes(1));

        var firstRotation = session.Rotate(
            1,
            "hash-1",
            "hash-2",
            _timeProvider.GetUtcNow());

        Assert.False(firstRotation.IsFailure);

        _timeProvider.Advance(
            TimeSpan.FromSeconds(11));

        var result = session.Rotate(
            1,
            "hash-1",
            "hash-3",
            _timeProvider.GetUtcNow());

        Assert.True(result.IsFailure);

        Assert.Equal(
            "Identity.SessionReuseDetected",
            result.Error!.Key);

        Assert.Equal(
            AuthSessionStatus.Revoked,
            session.Status);

        Assert.Equal(
            SessionRevocationReason.ReuseDetected,
            session.RevocationReason);
    }

    [Fact]
    public void RotateRejectsExpiredSession()
    {
        var session = CreateSession();

        var result = session.Rotate(
            1,
            "hash-1",
            "hash-2",
            session.ExpiresAt);

        Assert.True(result.IsFailure);

        Assert.Equal(
            "Identity.SessionExpired",
            result.Error!.Key);

        Assert.Equal(
            AuthSessionStatus.Active,
            session.Status);
    }

    [Fact]
    public void RotateDoesNotExtendBeyondAbsoluteExpiry()
    {
        var session = CreateSession();

        // Day 29: first rotation extends expiry to day 59.
        _timeProvider.Advance(
            TimeSpan.FromDays(29));

        var firstRotation = session.Rotate(
            1,
            "hash-1",
            "hash-2",
            _timeProvider.GetUtcNow());

        Assert.False(firstRotation.IsFailure);

        // Day 58: second rotation would extend to day 88.
        _timeProvider.Advance(
            TimeSpan.FromDays(29));

        var secondRotation = session.Rotate(
            2,
            "hash-2",
            "hash-3",
            _timeProvider.GetUtcNow());

        Assert.False(secondRotation.IsFailure);

        // Day 87: third rotation would extend to day 117,
        // but the absolute expiry is day 90.
        _timeProvider.Advance(
            TimeSpan.FromDays(29));

        var thirdRotation = session.Rotate(
            3,
            "hash-3",
            "hash-4",
            _timeProvider.GetUtcNow());

        Assert.False(thirdRotation.IsFailure);

        Assert.Equal(
            session.AbsoluteExpiresAt,
            session.ExpiresAt);
    }

    [Fact]
    public void RevokeIsIdempotent()
    {
        var session = CreateSession();

        _timeProvider.Advance(
            TimeSpan.FromMinutes(1));

        Assert.False(
            session.Revoke(
                SessionRevocationReason.SignedOut,
                _timeProvider.GetUtcNow()).IsFailure);

        _timeProvider.Advance(
            TimeSpan.FromMinutes(1));

        Assert.False(
            session.Revoke(
                SessionRevocationReason.SignedOut,
                _timeProvider.GetUtcNow()).IsFailure);

        Assert.Equal(
            AuthSessionStatus.Revoked,
            session.Status);

        Assert.Equal(
            SessionRevocationReason.SignedOut,
            session.RevocationReason);
    }

    private AuthSession CreateSession()
    {
        var result = AuthSession.Start(
            new AuthSessionId(Guid.CreateVersion7()),
            new Common.Domain.StronglyTypedIds.ApplicationUserId(
                Guid.CreateVersion7()),
            "device-1",
            "hash-1",
            _timeProvider.GetUtcNow());

        Assert.False(result.IsFailure);

        return result.Value
            ?? throw new InvalidOperationException(
                "AuthSession.Start returned no value for a successful result.");
    }

    private sealed class TestTimeProvider(
        DateTimeOffset initialUtc)
        : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtc;

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }

        public void Advance(TimeSpan duration)
        {
            _utcNow = _utcNow.Add(duration);
        }
    }
}
