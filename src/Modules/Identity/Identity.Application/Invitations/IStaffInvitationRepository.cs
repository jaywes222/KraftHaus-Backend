using Common.Domain.StronglyTypedIds;
using Common.Domain.Tenancy;
using Identity.Domain.Common;
using Identity.Domain.Invitations;
using Identity.Domain.Roles;

namespace Identity.Application.Invitations;

public interface IStaffInvitationRepository
{
    Task<StaffInvitation?> FindAsync(
        StaffInvitationId id,
        CancellationToken cancellationToken);

    /// <summary>Pending invitations issued on <paramref name="tenantId" />, newest first (read-only).</summary>
    Task<IReadOnlyList<StaffInvitation>> ListPendingAsync(
        TenantId tenantId,
        CancellationToken cancellationToken);

    /// <summary>True when the same person already has a pending invitation for the same role and scope.</summary>
    Task<bool> HasPendingAsync(
        TenantId tenantId,
        InvitationChannel channel,
        string destination,
        IdentityRole role,
        RoleScopeType scopeType,
        Guid? scopeId,
        CancellationToken cancellationToken);

    Task AddAsync(
        StaffInvitation invitation,
        CancellationToken cancellationToken);

    Task SaveAsync(
        StaffInvitation invitation,
        CancellationToken cancellationToken);
}
