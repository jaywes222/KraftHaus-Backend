using System.Security.Claims;
using Common.Domain.StronglyTypedIds;
using Common.Domain.Tenancy;
using Duende.IdentityServer;
using Duende.IdentityServer.Services;
using Identity.Application.Sessions;
using Identity.Domain.Common;
using Identity.Domain.Users;
using Identity.Infrastructure.Options;
using Identity.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Identity.Infrastructure.IdentityServer;

internal sealed class IdentityServerSessionTokenIssuer(
    IIdentityServerTools identityServerTools,
    IdentityDbContext db,
    IOptions<KraftHausIdentityServerOptions> options)
    : ISessionTokenIssuer
{
    private readonly KraftHausIdentityServerOptions _options =
        options.Value;

    public async Task<string> IssueAccessTokenAsync(
        ApplicationUserId userId,
        AuthSessionId sessionId,
        CancellationToken cancellationToken)
    {
        var user =
            await db.Users
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.Id == userId,
                    cancellationToken);

        if (user is null)
        {
            throw new InvalidOperationException(
                $"Identity user '{userId}' was not found.");
        }

        if (user.Status != UserStatus.Active)
        {
            throw new InvalidOperationException(
                $"Identity user '{userId}' is disabled.");
        }

        var claims = new List<Claim>
        {
            new(
                "sub",
                user.Id.Value.ToString()),

            new(
                "scope",
                _options.ApiScope),

            new(
                "aud",
                _options.ApiAudience),

            new(
                "sid",
                sessionId.Value.ToString()),

            new(
                "client_id",
                IdentityServerConfiguration.OtpClientId),

            // Spec 08 §6.1 calls this "scope" (platform | tenant); that name is taken by the OAuth scope claim
            // Duende validates, so the identity directory travels as "identity_scope".
            new(
                IdentityServerConfiguration.IdentityScopeClaim,
                user.Scope == IdentityScope.Platform ? "platform" : "tenant")
        };

        if (user.TenantId.HasValue)
        {
            claims.Add(
                new Claim(
                    "tid",
                    user.TenantId.Value.Value.ToString()));
        }

        return await identityServerTools.IssueJwtAsync(
            _options.AccessTokenLifetimeSeconds,
            _options.IssuerUri,
            claims,
            cancellationToken);
    }
}
