using Common.Domain.Events;
using Common.Domain.StronglyTypedIds;
using Identity.Domain.Common;
using Identity.Domain.Otp;
using Identity.Domain.Sessions;

namespace Identity.Domain.Events;

public sealed record OtpIssued(
    OtpChallengeId ChallengeId,
    OtpChannel Channel,
    string Destination,
    OtpPurpose Purpose,
    DateTimeOffset ExpiresAt) : DomainEvent;

public sealed record OtpAttemptFailed(
    OtpChallengeId ChallengeId,
    int AttemptCount) : DomainEvent;

public sealed record OtpLocked(
    OtpChallengeId ChallengeId) : DomainEvent;

public sealed record OtpExpired(
    OtpChallengeId ChallengeId) : DomainEvent;

public sealed record OtpSuperseded(
    OtpChallengeId ChallengeId) : DomainEvent;

public sealed record OtpVerified(
    OtpChallengeId ChallengeId) : DomainEvent;

public sealed record SessionStarted(
    AuthSessionId SessionId,
    ApplicationUserId UserId) : DomainEvent;

public sealed record SessionTokenRotated(
    AuthSessionId SessionId,
    long Generation) : DomainEvent;

public sealed record SessionRevoked(
    AuthSessionId SessionId,
    SessionRevocationReason Reason) : DomainEvent;
