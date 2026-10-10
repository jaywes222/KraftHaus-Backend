using Common.Domain.Tenancy;
using Identity.Domain.Common;
using Identity.Domain.Otp;
using Xunit;

namespace Identity.Tests;

public sealed class OtpChallengeTests
{
    private static readonly DateTimeOffset _now =
        new(2026, 10, 8, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public void IssueCreatesPendingChallengeWithFiveMinuteLifetime()
    {
        var result = Issue("hash");

        Assert.False(result.IsFailure);
        var challenge = result.Value!;

        Assert.Equal(OtpChallengeStatus.Pending, challenge.Status);
        Assert.Equal(_now, challenge.IssuedAt);
        Assert.Equal(_now.AddMinutes(5), challenge.ExpiresAt);
        Assert.Equal(0, challenge.AttemptCount);
    }

    [Fact]
    public void VerifyWithWrongCodePersistsAttemptStateInAggregate()
    {
        var challenge = Issue("expected").Value!;

        var result = challenge.Verify("wrong", _now.AddSeconds(1));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.OtpInvalid", result.Error!.Key);
        Assert.Equal(1, challenge.AttemptCount);
        Assert.Equal(OtpChallengeStatus.Pending, challenge.Status);
    }

    [Fact]
    public void VerifyLocksAfterFiveFailedAttempts()
    {
        var challenge = Issue("expected").Value!;

        for (var i = 1; i <= 5; i++)
        {
            var result = challenge.Verify(
                "wrong",
                _now.AddSeconds(i));

            Assert.True(result.IsFailure);
        }

        Assert.Equal(5, challenge.AttemptCount);
        Assert.Equal(OtpChallengeStatus.Locked, challenge.Status);
        Assert.Equal("Identity.OtpLocked", challenge.Verify("expected", _now.AddSeconds(6)).Error!.Key);
    }

    [Fact]
    public void VerifyWithCorrectCodeMarksChallengeVerified()
    {
        var challenge = Issue("expected").Value
            ?? throw new InvalidOperationException(
                "OTP challenge was not created.");

        var verificationTime = _now.AddSeconds(1);

        var result = challenge.Verify(
            "expected",
            verificationTime);

        Assert.False(result.IsFailure);
        Assert.Equal(
            OtpChallengeStatus.Verified,
            challenge.Status);
        Assert.Equal(
            verificationTime,
            challenge.VerifiedAt);
        Assert.Equal(
            1,
            challenge.AttemptCount);
    }

    [Fact]
    public void VerifyAfterExpiryMarksChallengeExpired()
    {
        var challenge = Issue("expected").Value!;

        var result = challenge.Verify(
            "expected",
            _now.Add(OtpChallenge.ChallengeLifetime));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.OtpExpired", result.Error!.Key);
        Assert.Equal(OtpChallengeStatus.Expired, challenge.Status);
    }

    [Fact]
    public void IssueRejectsResendInsideCooldown()
    {
        var previous = Issue("old").Value!;

        var result = OtpChallenge.Issue(
            new OtpChallengeId(Guid.CreateVersion7()),
            previous,
            OtpChannel.Phone,
            "+254712345678",
            OtpPurpose.SignIn,
            "new",
            _now.AddSeconds(30),
            new TenantId(Guid.CreateVersion7()));

        Assert.True(result.IsFailure);
        Assert.Equal("Identity.OtpResendTooSoon", result.Error!.Key);
        Assert.Equal(OtpChallengeStatus.Pending, previous.Status);
    }

    [Fact]
    public void IssueAfterCooldownSupersedesPreviousChallenge()
    {
        var previous = Issue("old").Value!;

        var result = OtpChallenge.Issue(
            new OtpChallengeId(Guid.CreateVersion7()),
            previous,
            OtpChannel.Phone,
            "+254712345678",
            OtpPurpose.SignIn,
            "new",
            _now.Add(OtpChallenge.ResendDelay),
            new TenantId(Guid.CreateVersion7()));

        Assert.False(result.IsFailure);
        Assert.Equal(OtpChallengeStatus.Superseded, previous.Status);
        Assert.Equal(OtpChallengeStatus.Pending, result.Value!.Status);
    }

    private static Common.Domain.ResultMonad.Result<OtpChallenge> Issue(string hash)
    {
        return OtpChallenge.Issue(
            new OtpChallengeId(Guid.CreateVersion7()),
            null,
            OtpChannel.Phone,
            "+254712345678",
            OtpPurpose.SignIn,
            hash,
            _now,
            new TenantId(Guid.CreateVersion7()));
    }
}
