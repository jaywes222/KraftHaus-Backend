using Common.Domain.StronglyTypedIds;
using Identity.Domain.Common;
using Identity.Domain.Invitations;

namespace Identity.Application.Invitations;

public interface IStaffInvitationRepository
{
    Task<StaffInvitation?> FindAsync(
        StaffInvitationId id,
        CancellationToken cancellationToken);

    Task AddAsync(
        StaffInvitation invitation,
        CancellationToken cancellationToken);

    Task SaveAsync(
        StaffInvitation invitation,
        CancellationToken cancellationToken);
}
