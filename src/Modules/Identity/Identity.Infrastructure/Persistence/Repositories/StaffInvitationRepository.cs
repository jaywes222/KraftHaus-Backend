using Common.Domain.Tenancy;
using Identity.Application.Invitations;
using Identity.Domain.Common;
using Identity.Domain.Invitations;
using Identity.Domain.Roles;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Persistence.Repositories;

internal sealed class StaffInvitationRepository(
    IdentityDbContext db)
    : IStaffInvitationRepository
{
    public Task<StaffInvitation?> FindAsync(
        StaffInvitationId id,
        CancellationToken cancellationToken)
    {
        return db.StaffInvitations
            .SingleOrDefaultAsync(
                x => x.Id == id,
                cancellationToken);
    }

    public async Task<IReadOnlyList<StaffInvitation>> ListPendingAsync(
        TenantId tenantId,
        CancellationToken cancellationToken)
    {
        return await db.StaffInvitations
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Status == StaffInvitationStatus.Pending)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public Task<bool> HasPendingAsync(
        TenantId tenantId,
        InvitationChannel channel,
        string destination,
        IdentityRole role,
        RoleScopeType scopeType,
        Guid? scopeId,
        CancellationToken cancellationToken)
    {
        return db.StaffInvitations.AnyAsync(
            x => x.TenantId == tenantId &&
                 x.Status == StaffInvitationStatus.Pending &&
                 x.Channel == channel &&
                 x.Destination == destination &&
                 x.Role == role &&
                 x.ScopeType == scopeType &&
                 x.ScopeId == scopeId,
            cancellationToken);
    }

    public Task AddAsync(
        StaffInvitation invitation,
        CancellationToken cancellationToken)
    {
        db.StaffInvitations.Add(invitation);

        return Task.CompletedTask;
    }

    public Task SaveAsync(
        StaffInvitation invitation,
        CancellationToken cancellationToken)
    {
        db.StaffInvitations.Update(invitation);

        return Task.CompletedTask;
    }
}
