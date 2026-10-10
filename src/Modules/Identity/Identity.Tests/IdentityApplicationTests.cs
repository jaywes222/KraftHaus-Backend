using Common.Application;
using Common.Application.Tenancy;
using Common.Domain.ResultMonad;
using Common.Domain.StronglyTypedIds;
using Common.Domain.Tenancy;
using Identity.Application.Abstractions;
using Identity.Application.Auditing;
using Identity.Application.Authorization;
using Identity.Application.Invitations;
using Identity.Application.Otp;
using Identity.Application.Sessions;
using Identity.Domain.Common;
using Identity.Domain.Invitations;
using Identity.Domain.Otp;
using Identity.Domain.Roles;
using Identity.Domain.Sessions;
using Identity.Domain.Users;
using Identity.Tests.Auditing;
using Xunit;

namespace Identity.Tests;

public sealed class IdentityApplicationTests
{
    private readonly RecordingIdentityAuditTrail _audit = new();

    private readonly StubCurrentTenant _platformHost =
        new(new TenantId(Guid.CreateVersion7()), IdentityScope.Platform);

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
            _clock,
            _audit,
            _platformHost);

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
            _clock,
            _audit,
            _platformHost);

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
            _clock.GetUtcNow(),
            _platformHost.TenantId);

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
            _clock,
            _audit,
            _platformHost);

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

        var rejection = Assert.Single(_audit.Rejections);

        Assert.Equal(
            IdentityAuditActions.OtpRequestRejected,
            rejection.Action);

        Assert.Equal(
            "Identity.OtpResendTooSoon",
            rejection.Error.Key);

        // The raw destination is handed over only to be fingerprinted; it is never part of the metadata.
        Assert.Equal(
            "+254712345678",
            rejection.DestinationToFingerprint);

        Assert.DoesNotContain(
            rejection.Metadata!.Values,
            v => v.Contains("712345678", StringComparison.Ordinal));
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
                _clock,
                _audit);

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

        var rejection = Assert.Single(_audit.Rejections);

        Assert.Equal(
            IdentityAuditActions.SessionRotationRejected,
            rejection.Action);

        Assert.Equal(
            session.Id.Value.ToString(),
            rejection.Target!.Id);
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
                _clock,
                _audit);

        var result =
            await service.ExecuteAsync(
                new RevokeAuthSessionCommand(
                    session.Id,
                    session.UserId),
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

        Assert.Empty(_audit.Rejections);

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
                _clock,
                _audit);

        var result =
            await service.ExecuteAsync(
                new RevokeAuthSessionCommand(
                    session.Id,
                    session.UserId),
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
                _clock,
                _audit);

        var result =
            await service.ExecuteAsync(
                new RevokeAuthSessionCommand(
                    missingSessionId,
                    ApplicationUserId.New()),
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

    [Fact]
    public async Task AcceptStaffInvitationVerifiesOtpAcceptsInvitationAndGrantsRole()
    {
        var user =
            CreateUser();

        var invitationResult =
            StaffInvitation.ForPhone(
                new StaffInvitationId(
                    Guid.CreateVersion7()),
                _platformHost.TenantId,
                new ApplicationUserId(
                    Guid.CreateVersion7()),
                user.Phone!.Value,
                IdentityRole.TenantAdmin,
                RoleScopeType.Tenant,
                Guid.CreateVersion7(),
                _clock.GetUtcNow());

        Assert.False(
            invitationResult.IsFailure);

        var invitation =
            invitationResult.Value!;

        var invitations =
            new FakeStaffInvitations(invitation);

        var users =
            new FakeIdentityUsers(user);

        var assignments =
            new FakeRoleAssignments();

        var otp =
            new FixedOtpProof(true);

        var unitOfWork =
            new RecordingUnitOfWork();

        var service =
            new AcceptStaffInvitationService(
                invitations,
                users,
                assignments,
                otp,
                unitOfWork,
                _clock,
                _audit,
                _platformHost);

        var result =
            await service.ExecuteAsync(
                new AcceptStaffInvitationCommand(
                    invitation.Id,
                    "123456"),
                CancellationToken.None);

        Assert.False(
            result.IsFailure);

        Assert.Equal(
            user.Id,
            result.Value!.UserId);

        Assert.Equal(
            IdentityRole.TenantAdmin,
            result.Value.Role);

        Assert.Equal(
            StaffInvitationStatus.Accepted,
            invitation.Status);

        Assert.NotNull(
            invitations.LastSaved);

        Assert.NotNull(
            assignments.LastAdded);

        Assert.Equal(
            IdentityRole.TenantAdmin,
            assignments.LastAdded.Role);

        Assert.Equal(
            RoleAssignmentStatus.Active,
            assignments.LastAdded.Status);

        Assert.Equal(
            1,
            otp.Calls);

        Assert.Equal(
            1,
            unitOfWork.SaveCount);
    }

    [Fact]
    public async Task AcceptStaffInvitationRejectsInvalidOtp()
    {
        var user =
            CreateUser();

        var invitation =
            StaffInvitation.ForPhone(
                new StaffInvitationId(
                    Guid.CreateVersion7()),
                _platformHost.TenantId,
                new ApplicationUserId(
                    Guid.CreateVersion7()),
                user.Phone!.Value,
                IdentityRole.TenantAdmin,
                RoleScopeType.Tenant,
                Guid.CreateVersion7(),
                _clock.GetUtcNow()).Value!;

        var invitations =
            new FakeStaffInvitations(invitation);

        var service =
            new AcceptStaffInvitationService(
                invitations,
                new FakeIdentityUsers(user),
                new FakeRoleAssignments(),
                new FixedOtpProof(false),
                new RecordingUnitOfWork(),
                _clock,
                _audit,
                _platformHost);

        var result =
            await service.ExecuteAsync(
                new AcceptStaffInvitationCommand(
                    invitation.Id,
                    "999999"),
                CancellationToken.None);

        Assert.True(
            result.IsFailure);

        Assert.Equal(
            "Identity.InvitationOtpInvalid",
            result.Error!.Key);

        Assert.Equal(
            StaffInvitationStatus.Pending,
            invitation.Status);
    }

    [Fact]
    public async Task AcceptStaffInvitationRejectsExpiredInvitation()
    {
        var user =
            CreateUser();

        var invitation =
            StaffInvitation.ForPhone(
                new StaffInvitationId(
                    Guid.CreateVersion7()),
                _platformHost.TenantId,
                new ApplicationUserId(
                    Guid.CreateVersion7()),
                user.Phone!.Value,
                IdentityRole.TenantAdmin,
                RoleScopeType.Tenant,
                Guid.CreateVersion7(),
                _clock.GetUtcNow()).Value!;

        _clock.Advance(
            TimeSpan.FromDays(7));

        var service =
            new AcceptStaffInvitationService(
                new FakeStaffInvitations(invitation),
                new FakeIdentityUsers(user),
                new FakeRoleAssignments(),
                new FixedOtpProof(true),
                new RecordingUnitOfWork(),
                _clock,
                _audit,
                _platformHost);

        var result =
            await service.ExecuteAsync(
                new AcceptStaffInvitationCommand(
                    invitation.Id,
                    "123456"),
                CancellationToken.None);

        Assert.True(
            result.IsFailure);

        Assert.Equal(
            "Identity.InvitationExpired",
            result.Error!.Key);

        Assert.Equal(
            StaffInvitationStatus.Pending,
            invitation.Status);
    }

    [Fact]
    public async Task AcceptStaffInvitationRejectsDisabledUser()
    {
        var user =
            CreateUser();

        Assert.False(
            user.Disable().IsFailure);

        var invitation =
            StaffInvitation.ForPhone(
                new StaffInvitationId(
                    Guid.CreateVersion7()),
                _platformHost.TenantId,
                new ApplicationUserId(
                    Guid.CreateVersion7()),
                user.Phone!.Value,
                IdentityRole.TenantAdmin,
                RoleScopeType.Tenant,
                Guid.CreateVersion7(),
                _clock.GetUtcNow()).Value!;

        var service =
            new AcceptStaffInvitationService(
                new FakeStaffInvitations(invitation),
                new FakeIdentityUsers(user),
                new FakeRoleAssignments(),
                new FixedOtpProof(true),
                new RecordingUnitOfWork(),
                _clock,
                _audit,
                _platformHost);

        var result =
            await service.ExecuteAsync(
                new AcceptStaffInvitationCommand(
                    invitation.Id,
                    "123456"),
                CancellationToken.None);

        Assert.True(
            result.IsFailure);

        Assert.Equal(
            "Identity.InvitationUserDisabled",
            result.Error!.Key);

        Assert.Equal(
            StaffInvitationStatus.Pending,
            invitation.Status);
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
                TenantId tenantId,
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

    private sealed class FakeStaffInvitations(
    StaffInvitation? invitation)
    : IStaffInvitationRepository
    {
        private readonly StaffInvitation? _invitation =
            invitation;

        public StaffInvitation? LastSaved { get; private set; }

        public Task<StaffInvitation?> FindAsync(
            StaffInvitationId id,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(
                _invitation is not null &&
                _invitation.Id == id
                    ? _invitation
                    : null);
        }

        public Task<IReadOnlyList<StaffInvitation>> ListPendingAsync(
            TenantId tenantId,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<StaffInvitation> result =
                _invitation is not null && _invitation.TenantId == tenantId ? [_invitation] : [];

            return Task.FromResult(result);
        }

        public Task<bool> HasPendingAsync(
            TenantId tenantId,
            InvitationChannel channel,
            string destination,
            IdentityRole role,
            RoleScopeType scopeType,
            Guid? scopeId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(false);
        }

        public Task AddAsync(
            StaffInvitation invitation,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task SaveAsync(
            StaffInvitation invitation,
            CancellationToken cancellationToken)
        {
            LastSaved = invitation;

            return Task.CompletedTask;
        }
    }

    private sealed class FakeRoleAssignments
    : IRoleAssignmentRepository
    {
        public RoleAssignment? LastAdded { get; private set; }

        public Task<RoleAssignment?> FindAsync(
            RoleAssignmentId id,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(LastAdded is not null && LastAdded.Id == id ? LastAdded : null);
        }

        public Task AddAsync(
            RoleAssignment assignment,
            CancellationToken cancellationToken)
        {
            LastAdded = assignment;

            return Task.CompletedTask;
        }

        public Task SaveAsync(
            RoleAssignment assignment,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RoleAssignment>>
            GetActiveForUserAsync(
                ApplicationUserId userId,
                CancellationToken cancellationToken)
        {
            IReadOnlyList<RoleAssignment> result =
                LastAdded is not null
                    ? [LastAdded]
                    : [];

            return Task.FromResult(result);
        }
    }

    private sealed class FixedOtpProof(
    bool result)
    : IOtpProofService
    {
        private readonly bool _result = result;

        public int Calls { get; private set; }

        public Task<bool> VerifyAsync(
            TenantId tenantId,
            OtpChannel channel,
            string destination,
            string code,
            CancellationToken cancellationToken)
        {
            Calls++;

            return Task.FromResult(_result);
        }
    }

    private ApplicationUser CreateUser()
    {
        var phoneResult =
            PhoneNumber.Create("0712345678");

        Assert.False(phoneResult.IsFailure);
        Assert.NotNull(phoneResult.Value);

        var termsResult =
            PolicyRef.Create(
                "terms",
                1,
                "terms-hash");

        Assert.False(termsResult.IsFailure);
        Assert.NotNull(termsResult.Value);

        var privacyResult =
            PolicyRef.Create(
                "privacy",
                1,
                "privacy-hash");

        Assert.False(privacyResult.IsFailure);
        Assert.NotNull(privacyResult.Value);

        var result =
            ApplicationUser.RegisterVerifiedPhone(
                new ApplicationUserId(
                    Guid.CreateVersion7()),
                IdentityScope.Platform,
                null,
                phoneResult.Value,
                "Michael",
                termsResult.Value,
                privacyResult.Value,
                _clock.GetUtcNow());

        Assert.False(result.IsFailure);
        Assert.NotNull(result.Value);

        return result.Value;
    }

    private sealed class FakeIdentityUsers(
    ApplicationUser user)
    : IIdentityUserRepository
    {
        private readonly ApplicationUser _user =
            user;

        public ApplicationUser? LastAdded { get; private set; }

        public ApplicationUser? LastSaved { get; private set; }

        public Task<ApplicationUser?> FindAsync(
            ApplicationUserId id,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<ApplicationUser?>(
                _user.Id == id
                    ? _user
                    : null);
        }

        public Task<ApplicationUser?> FindByPhoneAsync(
            IdentityRealm realm,
            PhoneNumber phone,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<ApplicationUser?>(
                InRealm(realm) && _user.Phone == phone
                    ? _user
                    : null);
        }

        public Task<ApplicationUser?> FindByEmailAsync(
            IdentityRealm realm,
            EmailAddress email,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<ApplicationUser?>(
                InRealm(realm) && _user.Email == email
                    ? _user
                    : null);
        }

        private bool InRealm(IdentityRealm realm) =>
            _user.Scope == realm.Scope &&
            _user.TenantId == realm.TenantId;

        public Task AddAsync(
            ApplicationUser user,
            CancellationToken cancellationToken)
        {
            LastAdded = user;

            return Task.CompletedTask;
        }

        public Task SaveAsync(
            ApplicationUser user,
            CancellationToken cancellationToken)
        {
            LastSaved = user;

            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task RevokeAuthSessionOwnedByAnotherUserIsNotFoundAndStaysActive()
    {
        var session =
            AuthSession.Start(
                new AuthSessionId(Guid.CreateVersion7()),
                ApplicationUserId.New(),
                "device-1",
                "hash-1",
                _clock.GetUtcNow()).Value!;

        var unitOfWork =
            new RecordingUnitOfWork();

        var service =
            new RevokeAuthSessionService(
                new FakeSessions(session),
                unitOfWork,
                _clock,
                _audit);

        var result =
            await service.ExecuteAsync(
                new RevokeAuthSessionCommand(
                    session.Id,
                    ApplicationUserId.New()),
                CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("NotFound", result.Error!.Key);
        Assert.Equal(AuthSessionStatus.Active, session.Status);
        Assert.Equal(0, unitOfWork.SaveCount);
        Assert.Equal(IdentityAuditActions.SessionRevokeRejected, Assert.Single(_audit.Rejections).Action);
    }

    private sealed class StubCurrentTenant(
        TenantId tenantId,
        IdentityScope identityScope)
        : ICurrentTenant
    {
        public bool IsResolved => true;

        public TenantId TenantId => tenantId;

        public IdentityScope IdentityScope => identityScope;
    }
}
