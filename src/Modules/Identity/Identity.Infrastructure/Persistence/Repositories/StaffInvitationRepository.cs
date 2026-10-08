using Identity.Application.Invitations;
using Identity.Domain.Common;
using Identity.Domain.Invitations;
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
