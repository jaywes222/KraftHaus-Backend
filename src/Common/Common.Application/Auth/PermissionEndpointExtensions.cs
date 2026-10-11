using Microsoft.AspNetCore.Builder;

namespace Common.Application.Auth;

/// <summary>
///     Shared entry point for permission-protected endpoints (Spec 08 §6.2). Any module can declare
///     <c>.RequirePermission("events:manage")</c> without referencing Identity; Identity's policy provider turns the
///     policy name back into a permission requirement and evaluates it against the caller's role assignments.
/// </summary>
public static class PermissionEndpointExtensions
{
    /// <summary>Prefix of every permission policy name. Identity's policy provider keys off it.</summary>
    public const string PolicyPrefix = "Permission:";

    public static TBuilder RequirePermission<TBuilder>(
        this TBuilder builder,
        string permission)
        where TBuilder : IEndpointConventionBuilder
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(permission);

        return builder.RequireAuthorization($"{PolicyPrefix}{permission}");
    }
}
