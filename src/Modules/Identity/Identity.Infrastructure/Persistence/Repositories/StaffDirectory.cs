using Identity.Application.Staff;
using Identity.Domain.Roles;
using Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Persistence.Repositories;

internal sealed class StaffDirectory(IdentityDbContext db) : IStaffDirectory
{
    public async Task<IReadOnlyList<StaffAssignmentRow>> ListActiveAssignmentsAsync(
        IdentityRealm realm,
        CancellationToken cancellationToken)
    {
        var rows = await db.RoleAssignments
            .AsNoTracking()
            .Where(x => x.Status == RoleAssignmentStatus.Active)
            .Join(
                db.Users.AsNoTracking().Where(u => u.Scope == realm.Scope && u.TenantId == realm.TenantId),
                assignment => assignment.UserId,
                user => user.Id,
                (assignment, user) => new { assignment, user })
            .ToListAsync(cancellationToken);

        return
        [
            .. rows.Select(x => new StaffAssignmentRow(
                x.user.Id,
                x.user.DisplayName,
                x.user.Phone?.Value,
                x.user.Email?.Value,
                x.user.Status,
                x.user.LastSignedInAt,
                x.assignment.Id,
                x.assignment.Role,
                x.assignment.ScopeType,
                x.assignment.ScopeId,
                x.assignment.GrantedAt))
        ];
    }
}
