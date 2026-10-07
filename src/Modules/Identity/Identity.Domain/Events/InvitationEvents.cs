using Common.Domain.Events;
using Common.Domain.StronglyTypedIds;
using Identity.Domain.Common;
using Identity.Domain.Invitations;
using Identity.Domain.Roles;

namespace Identity.Domain.Events;

public sealed record StaffInvited(
    StaffInvitationId InvitationId,
    ApplicationUserId InvitedByUserId,
    InvitationChannel Channel,
    string Destination,
    IdentityRole Role,
    RoleScopeType ScopeType,
    Guid? ScopeId) : DomainEvent;

public sealed record StaffInvitationAccepted(
    StaffInvitationId InvitationId,
    ApplicationUserId UserId,
    IdentityRole Role,
    RoleScopeType ScopeType,
    Guid? ScopeId) : DomainEvent;

public sealed record StaffInvitationRevoked(
    StaffInvitationId InvitationId) : DomainEvent;
