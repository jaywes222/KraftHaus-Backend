using System.Net;
using Common.Application.Auditing;
using Common.Application.Tenancy;
using Common.Domain.ResultMonad;
using Common.Domain.StronglyTypedIds;
using Identity.Application.Abstractions;
using Identity.Application.Auditing;
using Identity.Application.Authorization;
using Identity.Domain.Common;
using Identity.Domain.Roles;
using Identity.Domain.Users;

namespace Identity.Application.Staff;

public sealed record GrantRoleCommand(
    ApplicationUserId UserId,
    IdentityRole Role,
    Guid? KraftId);

/// <summary>Grants a staff role to an existing user of the host's realm. The scope comes from the host, never the body.</summary>
public sealed class GrantRoleService(
    IIdentityUserRepository users,
    IRoleAssignmentRepository assignments,
    IIdentityUnitOfWork unitOfWork,
    TimeProvider time,
    IIdentityAuditTrail auditTrail,
    ICurrentTenant currentTenant)
{
    public Task<Result<RoleAssignmentResult>> ExecuteAsync(GrantRoleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ExecuteCoreAsync(command, cancellationToken)
            .AuditRejectionAsync(
                auditTrail,
                error => new IdentityAuditRejection(IdentityAuditActions.RoleGrantRejected, error)
                {
                    SubjectUserId = command.UserId.Value,
                    Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["role"] = command.Role.ToString()
                    }
                },
                cancellationToken);
    }

    private async Task<Result<RoleAssignmentResult>> ExecuteCoreAsync(
        GrantRoleCommand command,
        CancellationToken cancellationToken)
    {
        if (!currentTenant.IsResolved)
        {
            return IdentityErrors.InvalidState("TenantUnresolved", "Granting a role requires a tenant host.");
        }

        var realm = IdentityRealm.ForHost(currentTenant.TenantId, currentTenant.IdentityScope);
        var user = await users.FindAsync(command.UserId, cancellationToken);

        // Users of another realm are invisible from this host.
        if (user is null || !user.IsIn(realm))
        {
            return Error.NotFound(nameof(command.UserId), command.UserId.Value);
        }

        if (user.Status != UserStatus.Active)
        {
            return IdentityErrors.InvalidState("UserDisabled", "Roles cannot be granted to a disabled user.");
        }

        var scope = StaffRoleScope.Resolve(command.Role, currentTenant.TenantId, command.KraftId);

        if (scope.IsFailure)
        {
            return scope.Error!;
        }

        var (scopeType, scopeId) = scope.Value;

        var active = await assignments.GetActiveForUserAsync(user.Id, cancellationToken);

        if (active.Any(x => x.Role == command.Role && x.ScopeType == scopeType && x.ScopeId == scopeId))
        {
            return new Error
            {
                Key = "Identity.RoleAlreadyGranted",
                Description = "The user already has this role at this scope.",
                StatusCode = HttpStatusCode.Conflict
            };
        }

        var granted = RoleAssignment.Grant(
            new RoleAssignmentId(Guid.CreateVersion7()),
            user.Id,
            command.Role,
            scopeType,
            scopeId,
            time.GetUtcNow());

        if (granted.IsFailure)
        {
            return granted.Error!;
        }

        await assignments.AddAsync(granted.Value!, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return RoleAssignmentResult.From(granted.Value!);
    }
}
