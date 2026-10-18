using System.Security.Claims;
using System.Text.Encodings.Web;
using Common.Application.Tenancy;
using Displays.Application.Abstractions;
using Displays.Application.Devices;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Displays.Infrastructure.Authentication;

/// <summary>
///     Screens authenticate with their device token (Spec 08 §6.1: "separate device token, long-lived, revocable"):
///     <c>Authorization: Device khd_…</c>, or <c>Bearer khd_…</c> (what the SignalR client sends from
///     <c>?access_token=</c>). The token is looked up by hash in the host's tenant database, cached for a short while;
///     revoking a screen evicts the cache entry, so the token stops working at once.
/// </summary>
public sealed class DisplayDeviceAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    DeviceAuthenticator authenticator,
    DeviceTokenCache cache,
    ICurrentTenant tenant)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "DisplayDevice";
    public const string DeviceIdClaim = "display_device";
    public const string EventIdClaim = "display_event";

    /// <summary>Only screens: this scheme, and a device id in the principal.</summary>
    public static AuthorizationPolicy Policy { get; } =
        new AuthorizationPolicyBuilder(SchemeName).RequireAuthenticatedUser().RequireClaim(DeviceIdClaim).Build();

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        var space = header.IndexOf(' ', StringComparison.Ordinal);
        if (space <= 0)
        {
            return AuthenticateResult.NoResult();
        }

        var kind = header[..space];
        var token = header[(space + 1)..].Trim();
        if (!(kind.Equals("Device", StringComparison.OrdinalIgnoreCase) || kind.Equals("Bearer", StringComparison.OrdinalIgnoreCase)) ||
            !DeviceTokens.LooksLikeToken(token) ||
            !tenant.IsResolved)
        {
            return AuthenticateResult.NoResult();
        }

        var hash = DeviceTokens.Hash(token);
        var identity = await cache.GetOrAddAsync(tenant.TenantId.Value, hash, () => authenticator.AuthenticateAsync(token, Context.RequestAborted));
        if (identity is null)
        {
            return AuthenticateResult.Fail("Unknown or revoked display token.");
        }

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(DeviceIdClaim, identity.DeviceId.ToString("D")),
                new Claim(EventIdClaim, identity.EventId.ToString("D"))
            ],
            SchemeName));

        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }
}

/// <summary>Short-lived cache of token → screen, per tenant. Misses are cached too (briefly) to blunt token guessing.</summary>
public sealed class DeviceTokenCache(IMemoryCache cache, ICurrentTenant tenant) : IDeviceTokenCache
{
    private static readonly TimeSpan Hit = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Miss = TimeSpan.FromSeconds(5);

    public async Task<DeviceIdentity?> GetOrAddAsync(Guid tenantId, string tokenHash, Func<Task<DeviceIdentity?>> load)
    {
        ArgumentNullException.ThrowIfNull(load);

        var key = Key(tenantId, tokenHash);
        if (cache.TryGetValue(key, out DeviceIdentity? cached))
        {
            return cached;
        }

        var identity = await load();
        cache.Set(key, identity, identity is null ? Miss : Hit);

        return identity;
    }

    public void Evict(string tokenHash)
    {
        if (tenant.IsResolved)
        {
            cache.Remove(Key(tenant.TenantId.Value, tokenHash));
        }
    }

    private static string Key(Guid tenantId, string tokenHash) => $"displays:token:{tenantId:N}:{tokenHash}";
}
