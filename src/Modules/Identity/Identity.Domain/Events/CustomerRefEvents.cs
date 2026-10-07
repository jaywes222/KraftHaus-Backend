using Common.Domain.Events;
using Identity.Domain.Common;
using Tenancy.Domain.Tenants;

namespace Identity.Domain.Events;

public sealed record CustomerRefCreated(
    CustomerRefId CustomerRefId,
    IdentityScope IdentityScope,
    string ExternalIdentityId) : DomainEvent;

public sealed record CustomerRefRefreshed(
    CustomerRefId CustomerRefId,
    IdentityScope IdentityScope,
    string ExternalIdentityId) : DomainEvent;
