using Identity.Domain.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Memory;

namespace Identity.Infrastructure.IdentityServer;

/// <summary>
///     Evicts the cached session status after a save that revoked a session, so sign-out and reuse detection reject
///     that session's access tokens immediately on this node (other nodes catch up within the cache window).
///     Eviction runs after the save completes, so a concurrent request cannot re-cache the pre-revocation state.
/// </summary>
internal sealed class SessionStatusCacheInvalidator(IMemoryCache cache) : SaveChangesInterceptor
{
    private readonly List<AuthSession> _revoked = [];

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Collect(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        Collect(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        Evict();

        return base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    public override int SavedChanges(
        SaveChangesCompletedEventData eventData,
        int result)
    {
        Evict();

        return base.SavedChanges(eventData, result);
    }

    private void Collect(DbContext? context)
    {
        _revoked.Clear();

        if (context is null)
        {
            return;
        }

        _revoked.AddRange(
            context.ChangeTracker
                .Entries<AuthSession>()
                .Where(x => x.State == EntityState.Modified && x.Entity.Status == AuthSessionStatus.Revoked)
                .Select(x => x.Entity));
    }

    private void Evict()
    {
        foreach (var session in _revoked)
        {
            cache.Remove(SessionStatusCache.Key(session.Id));
        }

        _revoked.Clear();
    }
}
