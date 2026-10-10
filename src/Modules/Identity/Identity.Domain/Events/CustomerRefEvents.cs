using Common.Domain.Events;
using Common.Domain.Tenancy;
using Identity.Domain.Common;

namespace Identity.Domain.Events;

public sealed record CustomerRefCreated(
    CustomerRefId CustomerRefId,
    IdentityScope IdentityScope,
    string ExternalIdentityId) : DomainEvent;

public sealed record CustomerRefRefreshed(
    CustomerRefId CustomerRefId,
    IdentityScope IdentityScope,
    string ExternalIdentityId) : DomainEvent;
