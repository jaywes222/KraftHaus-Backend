using Common.Domain.StronglyTypedIds;
using Identity.Application.Abstractions;
using Identity.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Persistence.Repositories;

internal sealed class IdentityUserRepository(
    IdentityDbContext db)
    : IIdentityUserRepository
{
    public Task<ApplicationUser?> FindByPhoneAsync(
        IdentityRealm realm,
        PhoneNumber phone,
        CancellationToken cancellationToken)
    {
        return db.Users
            .SingleOrDefaultAsync(
                x => x.Scope == realm.Scope &&
                     x.TenantId == realm.TenantId &&
                     x.Phone == phone,
                cancellationToken);
    }

    public Task<ApplicationUser?> FindByEmailAsync(
        IdentityRealm realm,
        EmailAddress email,
        CancellationToken cancellationToken)
    {
        return db.Users
            .SingleOrDefaultAsync(
                x => x.Scope == realm.Scope &&
                     x.TenantId == realm.TenantId &&
                     x.Email == email,
                cancellationToken);
    }

    public Task SaveAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        db.Users.Update(user);

        return Task.CompletedTask;
    }

    public async Task<ApplicationUser?> FindAsync(
        ApplicationUserId id,
        CancellationToken cancellationToken)
    {
        return await db.Users.FindAsync(
            [id],
            cancellationToken);
    }

    public async Task AddAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        await db.Users.AddAsync(
            user,
            cancellationToken);
    }
}
