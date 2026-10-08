using Duende.IdentityServer;
using Duende.IdentityServer.Configuration;
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
