using Identity.Domain.Common;
using Identity.Domain.Sessions;

namespace Identity.Application.Sessions;

public interface IAuthSessionRepository
{
    Task<AuthSession?> FindAsync(
        AuthSessionId id,
        CancellationToken cancellationToken);

    Task AddAsync(
        AuthSession session,
        CancellationToken cancellationToken);

    Task SaveAsync(
        AuthSession session,
        CancellationToken cancellationToken);
}
