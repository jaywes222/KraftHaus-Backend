using Duende.IdentityServer.Models;
using Microsoft.IdentityModel.Tokens;

namespace Identity.Infrastructure.IdentityServer;

public static class IdentityServerConfiguration
{
    public const string ApiScope = "krafthaus.api";
    public const string ApiResource = "krafthaus";

    /// <summary>
    ///     First-party client that owns the access tokens minted by the OTP sign-in and refresh endpoints.
    ///     Duende's local API validation resolves the token's <c>client_id</c> and rejects tokens without one.
    ///     The client has no usable grant at <c>/connect/token</c>: tokens are only issued in-process.
    /// </summary>
    public const string OtpClientId = "krafthaus-otp";

    /// <summary>Access-token claim naming the caller's identity directory: <c>platform</c> or <c>tenant</c>.</summary>
    public const string IdentityScopeClaim = "identity_scope";

    private const string InProcessOnlyGrantType = "urn:krafthaus:grant-type:in-process";

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
        },
        new Client
        {
            ClientId = OtpClientId,
            ClientName = "KraftHaus OTP sign-in",

            // No extension grant validator is registered for this grant type, so the token endpoint refuses it.
            AllowedGrantTypes = [InProcessOnlyGrantType],
            RequireClientSecret = false,
            AllowOfflineAccess = false,

            AllowedScopes = [ApiScope]
        }
    ];
}
