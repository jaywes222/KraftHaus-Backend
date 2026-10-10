using Common.Application.Auditing;
using Common.Application.Tenancy;
using Common.Domain.ResultMonad;
using Identity.Application.Abstractions;
using Identity.Application.Auditing;
using Identity.Application.Authorization;
using Identity.Domain.Common;
using Identity.Domain.Roles;
using Identity.Domain.Users;

namespace Identity.Application.Staff;

public sealed record RevokeRoleCommand(RoleAssignmentId AssignmentId);

public sealed class RevokeRoleService(
    IIdentityUserRepository users,
    IRoleAssignmentRepository assignments,
    IIdentityUnitOfWork unitOfWork,
    TimeProvider time,
    IIdentityAuditTrail auditTrail,
    ICurrentTenant currentTenant)
{
    public Task<Result> ExecuteAsync(RevokeRoleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ExecuteCoreAsync(command, cancellationToken)
            .AuditRejectionAsync(
                auditTrail,
                error => new IdentityAuditRejection(IdentityAuditActions.RoleRevokeRejected, error)
                {
                    Target = new AuditTarget(
                        IdentityAuditTargets.RoleAssignment,
                        command.AssignmentId.Value.ToString())
                },
                cancellationToken);
    }

    private async Task<Result> ExecuteCoreAsync(RevokeRoleCommand command, CancellationToken cancellationToken)
    {
        if (!currentTenant.IsResolved)
        {
            return IdentityErrors.InvalidState("TenantUnresolved", "Revoking a role requires a tenant host.");
        }

        var assignment = await assignments.FindAsync(command.AssignmentId, cancellationToken);
        var user = assignment is null ? null : await users.FindAsync(assignment.UserId, cancellationToken);
        var realm = IdentityRealm.ForHost(currentTenant.TenantId, currentTenant.IdentityScope);

        if (assignment is null ||
            user is null ||
            !user.IsIn(realm) ||
            !StaffRoleScope.BelongsToTenant(assignment, currentTenant.TenantId))
        {
            return Error.NotFound(nameof(command.AssignmentId), command.AssignmentId.Value);
        }

        var revoked = assignment.Revoke(time.GetUtcNow());

        if (revoked.IsFailure)
        {
            return revoked;
        }

        await assignments.SaveAsync(assignment, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success;
    }
}
