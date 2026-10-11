using Common.Application.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Identity.Infrastructure.Authorization;

public sealed class PermissionPolicyProvider(
    IOptions<AuthorizationOptions> options)
        : DefaultAuthorizationPolicyProvider(options)
{
    public const string PolicyPrefix =
        PermissionEndpointExtensions.PolicyPrefix;

    public override async Task<AuthorizationPolicy?>
        GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(
                PolicyPrefix,
                StringComparison.Ordinal))
        {
            return await base.GetPolicyAsync(
                policyName);
        }

        var permission =
            policyName[
                PolicyPrefix.Length..];

        return string.IsNullOrWhiteSpace(permission)
            ? null
            : new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(
                new PermissionRequirement(
                    permission))
            .Build();
    }
}
