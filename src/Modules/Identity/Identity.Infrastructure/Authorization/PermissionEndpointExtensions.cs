using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Identity.Infrastructure.Authorization;

public static class PermissionEndpointExtensions
{
    public static TBuilder RequirePermission<TBuilder>(
        this TBuilder builder,
        string permission)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            permission);

        return builder.RequireAuthorization(
            $"{PermissionPolicyProvider.PolicyPrefix}{permission}");
    }
}
