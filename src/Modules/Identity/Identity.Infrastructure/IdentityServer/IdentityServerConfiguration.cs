using Duende.IdentityServer.Models;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Infrastructure.IdentityServer;

public static class IdentityServerConfiguration
{
    public const string ApiScope = "krafthaus.api";
    public const string ApiResource = "krafthaus";

    public static IEnumerable<ApiScope> ApiScopes =>
    [
        new ApiScope(
            ApiScope,
            "KraftHaus API")
    ];

    public static IEnumerable<ApiResource> ApiResources => [
        new ApiResource(
            ApiResource,
            "KraftHaus API")
        {
            Scopes = [ApiScope]
        }
    ];

    public static IEnumerable<Client> GetClients(
        IdentityServerClientOptions options) =>
    [
        new Client
        {
            ClientId = "krafthaus-web",
            ClientName = "KraftHaus Web",

            AllowedGrantTypes = GrantTypes.Code,
            RequirePkce = true,
            RequireClientSecret = false,

            RedirectUris = options.RedirectUris.ToArray(),

            PostLogoutRedirectUris =
                options.PostLogoutRedirectUris.ToArray(),

            AllowedScopes =
            [
                "openid",
                "profile",
                ApiScope
            ],

            AllowOfflineAccess = true
        }
    ];
}
