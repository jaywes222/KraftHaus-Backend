using Common.Domain.StronglyTypedIds;
using Identity.Domain.Roles;

namespace Identity.Application.Authorization;

public interface IRoleAssignmentRepository
{
    Task<IReadOnlyList<RoleAssignment>> GetActiveForUserAsync(
        ApplicationUserId userId,
        CancellationToken cancellationToken);
}
