using Common.Domain.StronglyTypedIds;
using Identity.Application.Authorization;
using Identity.Domain.Roles;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Persistence.Repositories;

internal sealed class RoleAssignmentRepository(
    IdentityDbContext db)
    : IRoleAssignmentRepository
{
    public Task AddAsync(
        RoleAssignment assignment,
        CancellationToken cancellationToken)
    {
        db.RoleAssignments.Add(assignment);

        return Task.CompletedTask;
    }

    public Task SaveAsync(
        RoleAssignment assignment,
        CancellationToken cancellationToken)
    {
        db.RoleAssignments.Update(assignment);

        return Task.CompletedTask;
    }

    public async Task<IReadOnlyList<RoleAssignment>>
        GetActiveForUserAsync(
            ApplicationUserId userId,
            CancellationToken cancellationToken)
    {
        return await db.RoleAssignments
            .AsNoTracking()
            .Where(x =>
                x.UserId == userId &&
                x.Status == RoleAssignmentStatus.Active)
            .ToListAsync(cancellationToken);
    }
}
