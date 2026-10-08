using System.Reflection;
using Common.Application;
using Common.Domain.ResultMonad;
using Identity.Application.Abstractions;
using Identity.Application.Otp;
using Identity.Application.Sessions;
using Identity.Domain.Common;
using Identity.Domain.Otp;
using Identity.Domain.Sessions;
using Microsoft.VisualBasic;
using Xunit;
using static System.Net.Mime.MediaTypeNames;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Identity.Tests;

public sealed class IdentityApplicationTests
{
    private readonly TestClock _clock =
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
    public async Task RequestOtpPersistsBeforeSendingCode()
    {
        var challenges = new FakeOtpChallenges();
        var sender = new RecordingOtpSender(challenges);
        var unitOfWork = new RecordingUnitOfWork();

        var service = new RequestOtpService(
            challenges,
            new FixedCodeGenerator("123456"),
            new FixedCodeHasher(),
            sender,
            unitOfWork,
            _clock);

        var result = await service.ExecuteAsync(
            new RequestOtpCommand(
                OtpChannel.Phone,
                "0712 345 678",
                OtpPurpose.SignIn),
            CancellationToken.None);

        Assert.False(result.IsFailure);
        Assert.NotNull(challenges.LastAdded);
        Assert.Equal(1, unitOfWork.SaveCount);
        Assert.Equal(1, sender.Calls);
        Assert.Equal("+254712345678", sender.Destination);
        Assert.Equal("123456", sender.Code);
        Assert.True(sender.SaveWasCalledBeforeSend);
    }

    [Fact]
    public async Task RequestOtpNormalizesEmailBeforePersistenceAndSending()
    {
        var challenges = new FakeOtpChallenges();
        var sender = new RecordingOtpSender(challenges);

        var service = new RequestOtpService(
            challenges,
            new FixedCodeGenerator("123456"),
            new FixedCodeHasher(),
            sender,
            new RecordingUnitOfWork(),
            _clock);

        var result = await service.ExecuteAsync(
            new RequestOtpCommand(
                OtpChannel.Email,
                " user@example.com ",
                OtpPurpose.SignIn),
            CancellationToken.None);

        Assert.False(result.IsFailure);

        Assert.Equal(
            "USER@EXAMPLE.COM",
            challenges.LastAdded?.Destination);

        Assert.Equal(
            "USER@EXAMPLE.COM",
            sender.Destination);
    }

    [Fact]
    public async Task RequestOtpDoesNotSendWhenChallengeCannotBeIssued()
    {
        var previousResult = OtpChallenge.Issue(
            new OtpChallengeId(Guid.CreateVersion7()),
            null,
            OtpChannel.Phone,
            "+254712345678",
            OtpPurpose.SignIn,
            "old",
            _clock.GetUtcNow());

        Assert.False(previousResult.IsFailure);

        var previous = previousResult.Value;

        Assert.NotNull(previous);

        var challenges = new FakeOtpChallenges
        {
            Pending = previous
        };

        var sender = new RecordingOtpSender(challenges);
        var unitOfWork = new RecordingUnitOfWork();

        _clock.Advance(TimeSpan.FromSeconds(10));

        var service = new RequestOtpService(
            challenges,
            new FixedCodeGenerator("123456"),
            new FixedCodeHasher(),
            sender,
            unitOfWork,
            _clock);

        var result = await service.ExecuteAsync(
            new RequestOtpCommand(
                OtpChannel.Phone,
                "+254712345678",
                OtpPurpose.SignIn),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(
            "Identity.OtpResendTooSoon",
            result.Error!.Key);

        Assert.Equal(0, sender.Calls);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public void RefreshTokenServiceGeneratesUrlSafeTokenAndHashesDeterministically()
    {
        var service = new RefreshTokenService();

        var token = service.Generate();
        var hash = service.Hash(token);

        Assert.NotEmpty(token);
        Assert.DoesNotContain("=", token, StringComparison.Ordinal);
        Assert.DoesNotContain("+", token, StringComparison.Ordinal);
        Assert.DoesNotContain("/", token, StringComparison.Ordinal);
        Assert.Equal(64, hash.Length);
        Assert.True(service.Verify(token, hash));
        Assert.False(service.Verify(token + "x", hash));
    }

    [Fact]
    public async Task RotateAuthSessionPersistsReuseRevocation()
    {
        var sessionResult = AuthSession.Start(
            new AuthSessionId(Guid.CreateVersion7()),
            new Common.Domain.StronglyTypedIds.ApplicationUserId(
                Guid.CreateVersion7()),
            "device-1",
            "hash-1",
            _clock.GetUtcNow());

        Assert.False(sessionResult.IsFailure);

        var session = sessionResult.Value;

        Assert.NotNull(session);

        _clock.Advance(TimeSpan.FromMinutes(1));

        var rotationResult = session.Rotate(
            1,
            "hash-1",
            "hash-2",
            _clock.GetUtcNow());

        Assert.False(rotationResult.IsFailure);

        var sessions = new FakeSessions(session);
        var unitOfWork = new RecordingUnitOfWork();

        _clock.Advance(TimeSpan.FromMinutes(1));

        var service = new RotateAuthSessionService(
            sessions,
            new FixedRefreshTokens(),
            unitOfWork,
            _clock);

        var result = await service.ExecuteAsync(
            new RotateAuthSessionCommand(
                session.Id,
                "old-token"),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(
            "Identity.SessionReuseDetected",
            result.Error!.Key);

        Assert.Equal(
            AuthSessionStatus.Revoked,
            session.Status);

        Assert.Equal(
            1,
            unitOfWork.SaveCount);
    }

    private sealed class TestClock(
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

    private sealed class FixedCodeGenerator(
        string code) : IOtpCodeGenerator
    {
        private readonly string _code = code;

        public string Generate()
        {
            return _code;
        }
    }

    private sealed class FixedCodeHasher : IOtpCodeHasher
    {
        public string Hash(string code)
        {
            return code;
        }
    }

    private sealed class RecordingUnitOfWork : IIdentityUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task SaveChangesAsync(
            CancellationToken cancellationToken)
        {
            SaveCount++;

            return Task.CompletedTask;
        }
    }

    private sealed class FakeOtpChallenges : IOtpChallengeRepository
    {
        public OtpChallenge? Pending { get; set; }

        public OtpChallenge? LastAdded { get; private set; }

        public Task<OtpChallenge?> FindLatestPendingAsync(
            OtpChannel channel,
            string destination,
            OtpPurpose purpose,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Pending);
        }

        public Task<OtpChallenge?> FindAsync(
            OtpChallengeId challengeId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<OtpChallenge?>(LastAdded);
        }

        public Task AddAsync(
            OtpChallenge challenge,
            CancellationToken cancellationToken)
        {
            LastAdded = challenge;
            Pending = challenge;

            return Task.CompletedTask;
        }

        public Task SaveAsync(
            OtpChallenge challenge,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingOtpSender(
        FakeOtpChallenges challenges) : IOtpSender
    {
        private readonly FakeOtpChallenges _challenges = challenges;

        public int Calls { get; private set; }

        public string? Destination { get; private set; }

        public string? Code { get; private set; }

        public bool SaveWasCalledBeforeSend { get; private set; }

        public Task SendAsync(
            OtpChannel channel,
            string destination,
            string code,
            OtpPurpose purpose,
            CancellationToken cancellationToken)
        {
            Calls++;
            Destination = destination;
            Code = code;
            SaveWasCalledBeforeSend =
                _challenges.LastAdded is not null;

            return Task.CompletedTask;
        }
    }

    private sealed class FakeSessions(
        AuthSession session) : IAuthSessionRepository
    {
        private readonly AuthSession _session = session;

        public Task<AuthSession?> FindAsync(
            AuthSessionId id,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<AuthSession?>(
                _session.Id == id
                    ? _session
                    : null);
        }

        public Task AddAsync(
            AuthSession session,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task SaveAsync(
            AuthSession session,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class FixedRefreshTokens : IRefreshTokenService
    {
        public string Generate()
        {
            return "new-token";
        }

        public string Hash(string refreshToken)
        {
            return refreshToken switch
            {
                "new-token" => "hash-new",
                "current-token" => "hash-2",
                _ => "old-hash"
            };
        }

        public bool Verify(
            string refreshToken,
            string expectedHash)
        {
            return Hash(refreshToken) == expectedHash;
        }
    }
}
