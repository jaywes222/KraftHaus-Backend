using Duende.IdentityServer;
using Duende.IdentityServer.Configuration;
using Duende.IdentityServer.Hosting.LocalApiAuthentication;
using Duende.IdentityServer.Validation;
using Identity.Infrastructure.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Infrastructure.IdentityServer;

public static class IdentityServerSetup
{
    public static IServiceCollection AddKraftHausIdentityServer(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var clientOptions =
            configuration
                .GetSection(
                    IdentityServerClientOptions.SectionName)
                .Get<IdentityServerClientOptions>()
            ?? new IdentityServerClientOptions();

        services
            .AddOptions<IdentityServerClientOptions>()
            .BindConfiguration(
                IdentityServerClientOptions.SectionName)
            .Validate(
                options => options.RedirectUris.Count > 0,
                "At least one IdentityServer redirect URI must be configured.")
            .Validate(
                options => options.PostLogoutRedirectUris.Count > 0,
                "At least one IdentityServer post-logout redirect URI must be configured.")
            .ValidateOnStart();

        services
            .AddOptions<KraftHausIdentityServerOptions>()
            .BindConfiguration(
                KraftHausIdentityServerOptions.SectionName)
            .Validate(
                options =>
                    !string.IsNullOrWhiteSpace(options.IssuerUri),
                "IdentityServer IssuerUri must be configured.")
            .Validate(
                options =>
                    Uri.TryCreate(
                        options.IssuerUri,
                        UriKind.Absolute,
                        out var uri) &&
                    (
                        uri.Scheme == Uri.UriSchemeHttp ||
                        uri.Scheme == Uri.UriSchemeHttps
                    ),
                "IdentityServer IssuerUri must be an absolute HTTP or HTTPS URL.")
            .Validate(
                options =>
                    !string.IsNullOrWhiteSpace(options.ApiScope),
                "IdentityServer ApiScope must be configured.")
            .Validate(
                options =>
                    !string.IsNullOrWhiteSpace(options.ApiAudience),
                "IdentityServer ApiAudience must be configured.")
            .Validate(
                options =>
                    options.AccessTokenLifetimeSeconds > 0 &&
                    options.AccessTokenLifetimeSeconds <= 3600,
                "IdentityServer AccessTokenLifetimeSeconds must be between 1 and 3600 seconds.")
            .ValidateOnStart();

        var serverOptions =
            configuration
                .GetSection(KraftHausIdentityServerOptions.SectionName)
                .Get<KraftHausIdentityServerOptions>()
            ?? throw new InvalidOperationException(
                $"{KraftHausIdentityServerOptions.SectionName} configuration is required.");

        services
            .AddIdentityServer(options =>
            {
                options.EmitStaticAudienceClaim = true;

                // Tokens are issued with this issuer by IdentityServerSessionTokenIssuer; pin it so validation does
                // not depend on the request host (TestServer, reverse proxy, IIS bindings).
                options.IssuerUri = serverOptions.IssuerUri;

                // Interactive (cookie) sign-in keeps Duende's own scheme even though API requests authenticate
                // with bearer tokens by default (see AddAuthentication below).
                options.Authentication.CookieAuthenticationScheme =
                    IdentityServerConstants.DefaultCookieAuthenticationScheme;

                options.KeyManagement.SigningAlgorithms =
                [
                    new SigningAlgorithmOptions(
                        SecurityAlgorithms.EcdsaSha256)
                ];
            })
            .AddInMemoryApiScopes(
                IdentityServerConfiguration.ApiScopes)
            .AddInMemoryApiResources(
                IdentityServerConfiguration.ApiResources)
            .AddInMemoryClients(
                IdentityServerConfiguration.GetClients(
                    clientOptions));

        // Registered after AddIdentityServer so it replaces Duende's no-op DefaultCustomTokenValidator.
        services.AddScoped<ICustomTokenValidator, ActiveSessionTokenValidator>();

        // API requests authenticate with the access tokens this host issues (Spec 08 §6.1). Validation covers
        // signature, issuer, lifetime, the API scope, IsActive (disabled users) and the session revocation check.
        services
            .AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = IdentityServerConstants.LocalApi.AuthenticationScheme;
                options.DefaultChallengeScheme = IdentityServerConstants.LocalApi.AuthenticationScheme;
                options.DefaultForbidScheme = IdentityServerConstants.LocalApi.AuthenticationScheme;
            })
            .AddLocalApi(
                IdentityServerConstants.LocalApi.AuthenticationScheme,
                options =>
                {
                    options.ExpectedScope = serverOptions.ApiScope;
                    options.TokenMode = LocalApiTokenMode.BearerOnly;
                });

        services.AddMemoryCache();

        return services;
    }
}
