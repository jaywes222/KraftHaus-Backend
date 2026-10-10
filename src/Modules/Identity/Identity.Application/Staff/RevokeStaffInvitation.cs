using Common.Application.Auditing;
using Common.Application.Tenancy;
using Common.Domain.ResultMonad;
using Identity.Application.Abstractions;
using Identity.Application.Auditing;
using Identity.Application.Invitations;
using Identity.Domain.Common;

namespace Identity.Application.Staff;

public sealed record RevokeStaffInvitationCommand(StaffInvitationId InvitationId);

public sealed class RevokeStaffInvitationService(
    IStaffInvitationRepository invitations,
    IIdentityUnitOfWork unitOfWork,
    TimeProvider time,
    IIdentityAuditTrail auditTrail,
    ICurrentTenant currentTenant)
{
    public Task<Result> ExecuteAsync(RevokeStaffInvitationCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ExecuteCoreAsync(command, cancellationToken)
            .AuditRejectionAsync(
                auditTrail,
                error => new IdentityAuditRejection(IdentityAuditActions.InvitationRevokeRejected, error)
                {
                    Target = new AuditTarget(
                        IdentityAuditTargets.StaffInvitation,
                        command.InvitationId.Value.ToString())
                },
                cancellationToken);
    }

    private async Task<Result> ExecuteCoreAsync(RevokeStaffInvitationCommand command, CancellationToken cancellationToken)
    {
        if (!currentTenant.IsResolved)
        {
            return IdentityErrors.InvalidState("TenantUnresolved", "Revoking an invitation requires a tenant host.");
        }

        var invitation = await invitations.FindAsync(command.InvitationId, cancellationToken);

        if (invitation is null || invitation.TenantId != currentTenant.TenantId)
        {
            return Error.NotFound(nameof(command.InvitationId), command.InvitationId.Value);
        }

        var revoked = invitation.Revoke(time.GetUtcNow());

        if (revoked.IsFailure)
        {
            return revoked;
        }

        await invitations.SaveAsync(invitation, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success;
    }
}
