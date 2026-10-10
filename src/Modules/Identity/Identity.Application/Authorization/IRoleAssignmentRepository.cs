using Common.Domain.StronglyTypedIds;
using Identity.Domain.Common;
using Identity.Domain.Roles;

namespace Identity.Application.Authorization;

public interface IRoleAssignmentRepository
{
    Task<RoleAssignment?> FindAsync(
        RoleAssignmentId id,
        CancellationToken cancellationToken);

    Task AddAsync(
      RoleAssignment assignment,
      CancellationToken cancellationToken);

    Task SaveAsync(
        RoleAssignment assignment,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<RoleAssignment>> GetActiveForUserAsync(
        ApplicationUserId userId,
        CancellationToken cancellationToken);
}
