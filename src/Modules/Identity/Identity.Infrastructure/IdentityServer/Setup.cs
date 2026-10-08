using Duende.IdentityServer;
using Duende.IdentityServer.Configuration;
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

        services
            .AddIdentityServer(options =>
            {
                options.EmitStaticAudienceClaim = true;

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

        return services;
    }
}
