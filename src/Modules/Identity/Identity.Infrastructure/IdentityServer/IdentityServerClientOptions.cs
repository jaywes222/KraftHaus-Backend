namespace Identity.Infrastructure.IdentityServer;

public sealed class IdentityServerClientOptions
{
    public const string SectionName =
        "IdentityServer:Clients:KraftHausWeb";

    public IReadOnlyList<string> RedirectUris { get; init; } =
        [];

    public IReadOnlyList<string> PostLogoutRedirectUris { get; init; } =
        [];
}
