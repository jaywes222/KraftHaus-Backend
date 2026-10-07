using Common.Domain.Events;
using Common.Domain.StronglyTypedIds;
using Identity.Domain.Common;
using Identity.Domain.Roles;

namespace Identity.Domain.Events;

public sealed record RoleGranted(
    RoleAssignmentId AssignmentId,
    ApplicationUserId UserId,
    IdentityRole Role,
    RoleScopeType ScopeType,
    Guid? ScopeId) : DomainEvent;

public sealed record RoleRevoked(
    RoleAssignmentId AssignmentId,
    ApplicationUserId UserId,
    IdentityRole Role,
    RoleScopeType ScopeType,
    Guid? ScopeId) : DomainEvent;
