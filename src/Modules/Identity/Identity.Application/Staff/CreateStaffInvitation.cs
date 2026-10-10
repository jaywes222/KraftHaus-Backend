using System.Net;
using Common.Application.Auditing;
using Common.Application.Tenancy;
using Common.Domain.ResultMonad;
using Common.Domain.StronglyTypedIds;
using Identity.Application.Abstractions;
using Identity.Application.Auditing;
using Identity.Application.Invitations;
using Identity.Domain.Common;
using Identity.Domain.Invitations;
using Identity.Domain.Roles;

namespace Identity.Application.Staff;

public sealed record CreateStaffInvitationCommand(
    ApplicationUserId InvitedBy,
    InvitationChannel Channel,
    string Destination,
    IdentityRole Role,
    Guid? KraftId);

/// <summary>Invites a person to a staff role on the host's tenant (UF-022).</summary>
public sealed class CreateStaffInvitationService(
    IStaffInvitationRepository invitations,
    IStaffInvitationNotifier notifier,
    IIdentityUnitOfWork unitOfWork,
    TimeProvider time,
    IIdentityAuditTrail auditTrail,
    ICurrentTenant currentTenant)
{
    public Task<Result<StaffInvitationResult>> ExecuteAsync(
        CreateStaffInvitationCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return ExecuteCoreAsync(command, cancellationToken)
            .AuditRejectionAsync(
                auditTrail,
                error => new IdentityAuditRejection(IdentityAuditActions.InvitationCreateRejected, error)
                {
                    DestinationToFingerprint = command.Destination,
                    Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["role"] = command.Role.ToString(),
                        ["channel"] = command.Channel.ToString()
                    }
                },
                cancellationToken);
    }

    private async Task<Result<StaffInvitationResult>> ExecuteCoreAsync(
        CreateStaffInvitationCommand command,
        CancellationToken cancellationToken)
    {
        if (!currentTenant.IsResolved)
        {
            return IdentityErrors.InvalidState("TenantUnresolved", "Inviting staff requires a tenant host.");
        }

        var tenantId = currentTenant.TenantId;

        var destination = StaffDestination.Normalize(command.Channel, command.Destination);

        if (destination.IsFailure)
        {
            return destination.Error!;
        }

        var scope = StaffRoleScope.Resolve(command.Role, tenantId, command.KraftId);

        if (scope.IsFailure)
        {
            return scope.Error!;
        }

        var (scopeType, scopeId) = scope.Value;

        if (await invitations.HasPendingAsync(
                tenantId,
                command.Channel,
                destination.Value!,
                command.Role,
                scopeType,
                scopeId,
                cancellationToken))
        {
            return new Error
            {
                Key = "Identity.InvitationAlreadyPending",
                Description = "This person already has a pending invitation for that role.",
                StatusCode = HttpStatusCode.Conflict
            };
        }

        var now = time.GetUtcNow();
        var id = new StaffInvitationId(Guid.CreateVersion7());

        var created = command.Channel == InvitationChannel.Phone
            ? StaffInvitation.ForPhone(id, tenantId, command.InvitedBy, destination.Value!, command.Role, scopeType, scopeId, now)
            : StaffInvitation.ForEmail(id, tenantId, command.InvitedBy, destination.Value!, command.Role, scopeType, scopeId, now);

        if (created.IsFailure)
        {
            return created.Error!;
        }

        var invitation = created.Value!;

        await invitations.AddAsync(invitation, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        // After the commit: a failed notification never loses the invitation (it can be re-sent or shared by hand).
        await notifier.NotifyAsync(invitation, cancellationToken);

        return StaffInvitationResult.From(invitation, now);
    }
}
