using Common.Domain.StronglyTypedIds;
using Common.Domain.Tenancy;
using Identity.Application.Badges;
using Identity.Domain.Badges;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Persistence.Repositories;

internal sealed class StaffBadgeRepository(IdentityDbContext db) : IStaffBadgeRepository
{
    public Task<StaffBadge?> FindForUserAsync(TenantId tenantId, ApplicationUserId userId, CancellationToken cancellationToken) =>
        db.StaffBadges.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.UserId == userId, cancellationToken);

    public Task<StaffBadge?> FindByCodeForReadAsync(string code, CancellationToken cancellationToken) =>
        db.StaffBadges.AsNoTracking().SingleOrDefaultAsync(x => x.Code == code, cancellationToken);

    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken) =>
        db.StaffBadges.AsNoTracking().AnyAsync(x => x.Code == code, cancellationToken);

    public void Add(StaffBadge badge) => db.StaffBadges.Add(badge);
}
