using Common.Domain.Events;
using Common.Domain.StronglyTypedIds;
using Identity.Domain.Users;
using Tenancy.Domain.Tenants;

namespace Identity.Domain.Events;

public sealed record UserRegistered(
    ApplicationUserId UserId,
    IdentityScope Scope,
    string? TenantId,
    string? Phone,
    string? Email,
    string DisplayName) : DomainEvent;

public sealed record UserProfileChanged(
    ApplicationUserId UserId,
    IdentityScope Scope,
    string? TenantId,
    string? Phone,
    string? Email,
    string DisplayName) : DomainEvent;

public sealed record ConsentRecorded(
    ApplicationUserId UserId,
    ConsentKind Kind,
    bool Granted,
    string PolicyKey,
    int PolicyVersion,
    string PolicyTextHash) : DomainEvent;

public sealed record UserSignedIn(
    ApplicationUserId UserId,
    DateTimeOffset SignedInAt) : DomainEvent;

public sealed record UserDisabled(
    ApplicationUserId UserId) : DomainEvent;

public sealed record UserEnabled(
    ApplicationUserId UserId) : DomainEvent;
