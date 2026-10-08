using Identity.Application.Abstractions;

namespace Identity.Infrastructure.Persistence.Repositories;

internal sealed class IdentityUnitOfWork(
    IdentityDbContext db)
    : IIdentityUnitOfWork
{
    public Task SaveChangesAsync(
        CancellationToken cancellationToken)
    {
        return db.SaveChangesAsync(cancellationToken);
    }
}
