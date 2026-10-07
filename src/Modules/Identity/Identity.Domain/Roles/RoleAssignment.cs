using Common.Domain.Aggregates;
using Common.Domain.ResultMonad;
using Common.Domain.StronglyTypedIds;
using Identity.Domain.Common;
using Identity.Domain.Events;

namespace Identity.Domain.Roles;

public sealed class RoleAssignment(RoleAssignmentId id)
    : AggregateRoot<RoleAssignmentId>(id)
{
    private RoleAssignment()
        : this(new RoleAssignmentId(
            DefaultIdType.CreateVersion7()))
    {
    }

    public ApplicationUserId UserId { get; private set; }

    public IdentityRole Role { get; private set; }

    public RoleScopeType ScopeType { get; private set; }

    public Guid? ScopeId { get; private set; }

    public RoleAssignmentStatus Status { get; private set; }

    public DateTimeOffset GrantedAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public static Result<RoleAssignment> Grant(
        RoleAssignmentId id,
        ApplicationUserId userId,
        IdentityRole role,
        RoleScopeType scopeType,
        Guid? scopeId,
        DateTimeOffset now)
    {
        var scopeResult =
            RoleScopeRules.Validate(
                role,
                scopeType,
                scopeId);

        if (scopeResult.IsFailure)
        {
            return scopeResult.Error!;
        }

        var assignment = new RoleAssignment(id)
        {
            UserId = userId,
            Role = role,
            ScopeType = scopeType,
            ScopeId = scopeId,
            Status = RoleAssignmentStatus.Active,
            GrantedAt = now
        };

        assignment.RaiseEvent(
            new RoleGranted(
                assignment.Id,
                assignment.UserId,
                assignment.Role,
                assignment.ScopeType,
                assignment.ScopeId));

        return assignment;
    }

    public Result Revoke(DateTimeOffset now)
    {
        if (Status == RoleAssignmentStatus.Revoked)
        {
            return new Error
            {
                Key = "Identity.RoleAlreadyRevoked",
                Description =
                    "The role assignment has already been revoked."
            };
        }

        Status = RoleAssignmentStatus.Revoked;
        RevokedAt = now;

        RaiseEvent(
            new RoleRevoked(
                Id,
                UserId,
                Role,
                ScopeType,
                ScopeId));

        return Result.Success;
    }
}
