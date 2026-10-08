using Common.Application;
using Common.Domain.ResultMonad;
using Identity.Application.Abstractions;
using Identity.Application.Otp;
using Identity.Application.Sessions;
using Identity.Domain.Common;
using Identity.Domain.Otp;
using Identity.Domain.Sessions;
using Xunit;

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

        Assert.Equal(
            0,
            sender.Calls);

        Assert.Equal(
            0,
            unitOfWork.SaveCount);
    }

    [Fact]
    public void RefreshTokenServiceGeneratesSecretAndHashesDeterministically()
    {
        var service = new RefreshTokenService();

        var secret =
            service.GenerateSecret();

        var hash =
            service.HashSecret(secret);

        Assert.NotEmpty(secret);

        Assert.DoesNotContain(
            "=",
            secret,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "+",
            secret,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "/",
            secret,
            StringComparison.Ordinal);

        Assert.Equal(
            64,
            hash.Length);

        Assert.True(
            service.VerifySecret(
                secret,
                hash));

        Assert.False(
            service.VerifySecret(
                secret + "x",
                hash));
    }

    [Fact]
    public void RefreshTokenServiceCreatesAndParsesStructuredToken()
    {
        var service =
            new RefreshTokenService();

        var sessionId =
            new AuthSessionId(
                Guid.CreateVersion7());

        var secret =
            service.GenerateSecret();

        var token =
            service.Create(
                sessionId,
                7,
                secret);

        var parsed =
            service.TryParse(
                token,
                out var parts);

        Assert.True(parsed);

        Assert.Equal(
            sessionId,
            parts.SessionId);

        Assert.Equal(
            7,
            parts.Generation);

        Assert.Equal(
            secret,
            parts.Secret);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("abc.def")]
    [InlineData("abc.def.ghi.jkl")]
    [InlineData("not-a-guid.1.secret")]
    [InlineData(
        "00000000-0000-0000-0000-000000000000.zero.secret")]
    public void RefreshTokenServiceRejectsMalformedTokens(
        string token)
    {
        var service =
            new RefreshTokenService();

        Assert.False(
            service.TryParse(
                token,
                out _));
    }

    [Fact]
    public void RefreshTokenHashIsIndependentOfTokenMetadata()
    {
        var service =
            new RefreshTokenService();

        var secret =
            service.GenerateSecret();

        var tokenA =
            service.Create(
                new AuthSessionId(
                    Guid.CreateVersion7()),
                1,
                secret);

        var tokenB =
            service.Create(
                new AuthSessionId(
                    Guid.CreateVersion7()),
                99,
                secret);

        Assert.NotEqual(
            tokenA,
            tokenB);

        Assert.Equal(
            service.HashSecret(secret),
            service.HashSecret(secret));
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

        Assert.False(
            sessionResult.IsFailure);

        var session =
            sessionResult.Value;

        Assert.NotNull(session);

        _clock.Advance(
            TimeSpan.FromMinutes(1));

        var rotationResult =
            session.Rotate(
                1,
                "hash-1",
                "hash-2",
                _clock.GetUtcNow());

        Assert.False(
            rotationResult.IsFailure);

        var sessions =
            new FakeSessions(session);

        var unitOfWork =
            new RecordingUnitOfWork();

        _clock.Advance(
            TimeSpan.FromMinutes(1));

        var service =
            new RotateAuthSessionService(
                sessions,
                new FixedRefreshTokens(session.Id),
                unitOfWork,
                _clock);

        var result =
            await service.ExecuteAsync(
                new RotateAuthSessionCommand(
                    session.Id,
                    "old-token"),
                CancellationToken.None);

        Assert.True(
            result.IsFailure);

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

    [Fact]
    public async Task RevokeAuthSessionRevokesSessionAndPersists()
    {
        var sessionResult =
            AuthSession.Start(
                new AuthSessionId(
                    Guid.CreateVersion7()),
                new Common.Domain.StronglyTypedIds.ApplicationUserId(
                    Guid.CreateVersion7()),
                "device-1",
                "hash-1",
                _clock.GetUtcNow());

        Assert.False(
            sessionResult.IsFailure);

        var session =
            sessionResult.Value!;

        var sessions =
            new FakeSessions(session);

        var unitOfWork =
            new RecordingUnitOfWork();

        var service =
            new RevokeAuthSessionService(
                sessions,
                unitOfWork,
                _clock);

        var result =
            await service.ExecuteAsync(
                new RevokeAuthSessionCommand(
                    session.Id),
                CancellationToken.None);

        Assert.False(
            result.IsFailure);

        Assert.Equal(
            AuthSessionStatus.Revoked,
            session.Status);

        Assert.Equal(
            SessionRevocationReason.SignedOut,
            session.RevocationReason);

        Assert.NotNull(
            session.RevokedAt);

        Assert.Equal(
            1,
            unitOfWork.SaveCount);

        Assert.Equal(
            session,
            sessions.LastSaved);
    }

    [Fact]
    public async Task RevokeAuthSessionIsIdempotentForAlreadyRevokedSession()
    {
        var sessionResult =
            AuthSession.Start(
                new AuthSessionId(
                    Guid.CreateVersion7()),
                new Common.Domain.StronglyTypedIds.ApplicationUserId(
                    Guid.CreateVersion7()),
                "device-1",
                "hash-1",
                _clock.GetUtcNow());

        Assert.False(
            sessionResult.IsFailure);

        var session =
            sessionResult.Value!;

        var firstRevoke =
            session.Revoke(
                SessionRevocationReason.SignedOut,
                _clock.GetUtcNow());

        Assert.False(
            firstRevoke.IsFailure);

        var originalRevokedAt =
            session.RevokedAt;

        var sessions =
            new FakeSessions(session);

        var unitOfWork =
            new RecordingUnitOfWork();

        _clock.Advance(
            TimeSpan.FromMinutes(1));

        var service =
            new RevokeAuthSessionService(
                sessions,
                unitOfWork,
                _clock);

        var result =
            await service.ExecuteAsync(
                new RevokeAuthSessionCommand(
                    session.Id),
                CancellationToken.None);

        Assert.False(
            result.IsFailure);

        Assert.Equal(
            AuthSessionStatus.Revoked,
            session.Status);

        Assert.Equal(
            SessionRevocationReason.SignedOut,
            session.RevocationReason);

        Assert.Equal(
            originalRevokedAt,
            session.RevokedAt);

        Assert.Equal(
            1,
            unitOfWork.SaveCount);
    }

    [Fact]
    public async Task RevokeAuthSessionReturnsNotFoundWhenSessionDoesNotExist()
    {
        var missingSessionId =
            new AuthSessionId(
                Guid.CreateVersion7());

        var sessions =
            new FakeSessions(null);

        var unitOfWork =
            new RecordingUnitOfWork();

        var service =
            new RevokeAuthSessionService(
                sessions,
                unitOfWork,
                _clock);

        var result =
            await service.ExecuteAsync(
                new RevokeAuthSessionCommand(
                    missingSessionId),
                CancellationToken.None);

        Assert.True(
            result.IsFailure);

        Assert.Equal(
            "NotFound",
            result.Error!.Key);

        Assert.Equal(
            0,
            unitOfWork.SaveCount);
    }

    private sealed class TestClock(
        DateTimeOffset initialUtc)
        : TimeProvider
    {
        private DateTimeOffset _utcNow =
            initialUtc;

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }

        public void Advance(
            TimeSpan duration)
        {
            _utcNow =
                _utcNow.Add(duration);
        }
    }

    private sealed class FixedCodeGenerator(
        string code)
        : IOtpCodeGenerator
    {
        private readonly string _code =
            code;

        public string Generate()
        {
            return _code;
        }
    }

    private sealed class FixedCodeHasher
        : IOtpCodeHasher
    {
        public string Hash(
            string code)
        {
            return code;
        }
    }

    private sealed class RecordingUnitOfWork
        : IIdentityUnitOfWork
    {
        public int SaveCount
        {
            get;
            private set;
        }

        public Task SaveChangesAsync(
            CancellationToken cancellationToken)
        {
            SaveCount++;

            return Task.CompletedTask;
        }
    }

    private sealed class FakeOtpChallenges
        : IOtpChallengeRepository
    {
        public OtpChallenge? Pending
        {
            get;
            set;
        }

        public OtpChallenge? LastAdded
        {
            get;
            private set;
        }

        public Task<OtpChallenge?>
            FindLatestPendingAsync(
                OtpChannel channel,
                string destination,
                OtpPurpose purpose,
                CancellationToken cancellationToken)
        {
            return Task.FromResult(
                Pending);
        }

        public Task<OtpChallenge?>
            FindAsync(
                OtpChallengeId challengeId,
                CancellationToken cancellationToken)
        {
            return Task.FromResult<OtpChallenge?>(
                LastAdded);
        }

        public Task AddAsync(
            OtpChallenge challenge,
            CancellationToken cancellationToken)
        {
            LastAdded =
                challenge;

            Pending =
                challenge;

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
        FakeOtpChallenges challenges)
        : IOtpSender
    {
        private readonly FakeOtpChallenges
            _challenges =
                challenges;

        public int Calls
        {
            get;
            private set;
        }

        public string? Destination
        {
            get;
            private set;
        }

        public string? Code
        {
            get;
            private set;
        }

        public bool SaveWasCalledBeforeSend
        {
            get;
            private set;
        }

        public Task SendAsync(
            OtpChannel channel,
            string destination,
            string code,
            OtpPurpose purpose,
            CancellationToken cancellationToken)
        {
            Calls++;

            Destination =
                destination;

            Code =
                code;

            SaveWasCalledBeforeSend =
                _challenges.LastAdded is not null;

            return Task.CompletedTask;
        }
    }

    private sealed class FakeSessions(
        AuthSession? session)
        : IAuthSessionRepository
    {
        private readonly AuthSession? _session =
            session;

        public AuthSession? LastSaved
        {
            get;
            private set;
        }

        public Task<AuthSession?>
            FindAsync(
                AuthSessionId id,
                CancellationToken cancellationToken)
        {
            return Task.FromResult(
                _session is not null &&
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
            LastSaved =
                session;

            return Task.CompletedTask;
        }
    }

    private sealed class FixedRefreshTokens(
        AuthSessionId sessionId)
        : IRefreshTokenService
    {
        private readonly AuthSessionId
            _sessionId =
                sessionId;

        public string GenerateSecret()
        {
            return "new-secret";
        }

        public string Create(
            AuthSessionId sessionId,
            long generation,
            string secret)
        {
            return string.Join(
                '.',
                sessionId.Value,
                generation,
                secret);
        }

        public bool TryParse(
            string refreshToken,
            out RefreshTokenParts parts)
        {
            parts = default;

            if (refreshToken != "old-token")
            {
                return false;
            }

            parts =
                new RefreshTokenParts(
                    _sessionId,
                    1,
                    "old-secret");

            return true;
        }

        public string HashSecret(
            string secret)
        {
            return secret switch
            {
                "old-secret" =>
                    "hash-1",

                "new-secret" =>
                    "hash-new",

                _ =>
                    "unknown-hash"
            };
        }

        public bool VerifySecret(
            string secret,
            string expectedHash)
        {
            return HashSecret(secret)
                == expectedHash;
        }
    }
}
