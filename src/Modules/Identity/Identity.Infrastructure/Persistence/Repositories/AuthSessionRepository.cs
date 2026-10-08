using Identity.Application.Sessions;
using Identity.Domain.Common;
using Identity.Domain.Sessions;
using Microsoft.EntityFrameworkCore;

namespace Identity.Infrastructure.Persistence.Repositories;

internal sealed class AuthSessionRepository(
    IdentityDbContext db)
    : IAuthSessionRepository
{
    public Task<AuthSession?> FindAsync(
        AuthSessionId id,
        CancellationToken cancellationToken)
    {
        return db.Sessions
            .SingleOrDefaultAsync(
                x => x.Id == id,
                cancellationToken);
    }

    public Task AddAsync(
        AuthSession session,
        CancellationToken cancellationToken)
    {
        db.Sessions.Add(session);

        return Task.CompletedTask;
    }

    public Task SaveAsync(
        AuthSession session,
        CancellationToken cancellationToken)
    {
        db.Sessions.Update(session);

        return Task.CompletedTask;
    }
}
