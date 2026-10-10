using Duende.IdentityServer.Validation;
using Identity.Domain.Common;
using Identity.Domain.Sessions;
using Identity.Infrastructure.Options;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Identity.Infrastructure.IdentityServer;

/// <summary>
///     Rejects access tokens whose session (<c>sid</c>) is no longer active, so sign-out and refresh-token reuse
///     detection take effect within <see cref="KraftHausIdentityServerOptions.SessionStatusCacheSeconds" /> instead of
///     only when the 10 minute token expires (Spec 08 §6.1). It also rejects tokens whose session belongs to a
///     different subject.
/// </summary>
internal sealed class ActiveSessionTokenValidator(
    IdentityDbContext db,
    IMemoryCache cache,
    IOptions<KraftHausIdentityServerOptions> options,
    TimeProvider time)
    : ICustomTokenValidator
{
    private const string SessionClaim = "sid";
    private const string SubjectClaim = "sub";

    public async Task<TokenValidationResult> ValidateAccessTokenAsync(
        TokenValidationResult result,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsError)
        {
            return result;
        }

        var claims = result.Claims ?? [];
        var sid = claims.FirstOrDefault(x => x.Type == SessionClaim)?.Value;
        var sub = claims.FirstOrDefault(x => x.Type == SubjectClaim)?.Value;

        if (!Guid.TryParse(sid, out var sessionId) ||
            !Guid.TryParse(sub, out var userId) ||
            !await IsActiveAsync(new AuthSessionId(sessionId), userId, ct))
        {
            result.IsError = true;
            result.Error = "invalid_token";
            result.ErrorDescription = "The session is no longer active.";
        }

        return result;
    }

    public Task<TokenValidationResult> ValidateIdentityTokenAsync(
        TokenValidationResult result,
        CancellationToken ct) =>
        Task.FromResult(result);

    private async Task<bool> IsActiveAsync(
        AuthSessionId sessionId,
        Guid userId,
        CancellationToken ct)
    {
        var ttl = TimeSpan.FromSeconds(options.Value.SessionStatusCacheSeconds);

        if (ttl > TimeSpan.Zero &&
            cache.TryGetValue(SessionStatusCache.Key(sessionId), out SessionStatus? cached) &&
            cached is not null)
        {
            return cached.IsActiveFor(userId, time.GetUtcNow());
        }

        var status = await db.Sessions
            .AsNoTracking()
            .Where(x => x.Id == sessionId)
            .Select(x => new SessionStatus(
                x.UserId.Value,
                x.Status == AuthSessionStatus.Active,
                x.ExpiresAt,
                x.AbsoluteExpiresAt))
            .SingleOrDefaultAsync(ct);

        if (status is null)
        {
            return false;
        }

        if (ttl > TimeSpan.Zero)
        {
            cache.Set(SessionStatusCache.Key(sessionId), status, ttl);
        }

        return status.IsActiveFor(userId, time.GetUtcNow());
    }

    private sealed record SessionStatus(
        Guid UserId,
        bool Active,
        DateTimeOffset ExpiresAt,
        DateTimeOffset AbsoluteExpiresAt)
    {
        public bool IsActiveFor(Guid userId, DateTimeOffset now) =>
            Active &&
            UserId == userId &&
            now < ExpiresAt &&
            now < AbsoluteExpiresAt;
    }
}

/// <summary>Cache key shared by the validator and the session repository (which evicts on revocation).</summary>
internal static class SessionStatusCache
{
    public static string Key(AuthSessionId sessionId) =>
        $"identity:session-status:{sessionId.Value:N}";
}
