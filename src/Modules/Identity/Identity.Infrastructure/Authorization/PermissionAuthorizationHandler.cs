using System.Globalization;
using System.Net;
using System.Security.Claims;
using Common.Application.Auditing;
using Common.Application.Tenancy;
using Common.Domain.ResultMonad;
using Common.Domain.StronglyTypedIds;
using Identity.Application.Auditing;
using Identity.Application.Authorization;
using Identity.Domain.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Identity.Infrastructure.Authorization;

public sealed class PermissionAuthorizationHandler(
    IPermissionAuthorizationService permissions,
    IIdentityAuditTrail auditTrail)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var cancellationToken =
            context.Resource is HttpContext http
                ? http.RequestAborted
                : CancellationToken.None;

        if (!IdentityPermission.All.Contains(
                requirement.Permission))
        {
            await RecordDeniedAsync(
                "Identity.UnknownPermission",
                requirement,
                null,
                null,
                cancellationToken);

            return;
        }

        var subject =
            context.User.FindFirstValue("sub");

        if (!Guid.TryParse(
                subject,
                out var userId))
        {
            return;
        }

        var scope =
            ResolveScope(context);

        if (scope is null)
        {
            await RecordDeniedAsync(
                "Identity.PermissionScopeUnresolved",
                requirement,
                userId,
                null,
                cancellationToken);

            return;
        }

        var allowed =
            await permissions.HasPermissionAsync(
                new ApplicationUserId(userId),
                requirement.Permission,
                scope.Value,
                cancellationToken);

        if (allowed)
        {
            context.Succeed(requirement);
            return;
        }

        await RecordDeniedAsync(
            "Identity.PermissionDenied",
            requirement,
            userId,
            scope.Value,
            cancellationToken);
    }

    private Task RecordDeniedAsync(
        string reasonCode,
        PermissionRequirement requirement,
        Guid? userId,
        PermissionScope? scope,
        CancellationToken cancellationToken)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["permission"] = requirement.Permission
        };

        if (scope is { } resolved)
        {
            metadata["scopeType"] = resolved.ScopeType.ToString();

            if (resolved.ScopeId is { } scopeId)
            {
                metadata["scopeId"] = scopeId.ToString("D", CultureInfo.InvariantCulture);
            }
        }

        return auditTrail.RecordRejectedAsync(
            new IdentityAuditRejection(
                IdentityAuditActions.AuthorizationDenied,
                new Error
                {
                    Key = reasonCode,
                    StatusCode = HttpStatusCode.Forbidden
                })
            {
                SubjectUserId = userId,
                Target = new AuditTarget(
                    IdentityAuditTargets.Permission,
                    requirement.Permission),
                Metadata = metadata
            },
            cancellationToken);
    }

    /// <summary>
    ///     The scope a request asks for. On a tenant host the tenant always comes from the host (Spec 08 §5.3): a
    ///     <c>tenantId</c> route value naming another tenant, or a token issued for another tenant (<c>tid</c>), makes
    ///     the scope unresolvable, which denies. Without a resolved tenant the route values are used as before.
    /// </summary>
    private static PermissionScope? ResolveScope(
        AuthorizationHandlerContext context)
    {
        if (context.Resource is HttpContext httpContext)
        {
            var currentTenant = httpContext.RequestServices?.GetService<ICurrentTenant>();

            if (currentTenant is { IsResolved: true })
            {
                var hostTenant = currentTenant.TenantId.Value;
                var tokenTenant = context.User.FindFirstValue("tid");

                if ((tokenTenant is not null &&
                     (!Guid.TryParse(tokenTenant, out var tid) || tid != hostTenant)) ||
                    (TryGetGuidRouteValue(httpContext, "tenantId", out var routeTenant) && routeTenant != hostTenant))
                {
                    return null;
                }
            }

            if (TryGetGuidRouteValue(
                    httpContext,
                    "kraftId",
                    out var kraftId))
            {
                return PermissionScope.Kraft(
                    kraftId,
                    currentTenant is { IsResolved: true } ? currentTenant.TenantId.Value : null);
            }

            if (currentTenant is { IsResolved: true })
            {
                return PermissionScope.Tenant(currentTenant.TenantId.Value);
            }

            return TryGetGuidRouteValue(
                    httpContext,
                    "tenantId",
                    out var tenantId)
                ? PermissionScope.Tenant(tenantId)
                : PermissionScope.Platform();
        }

        return context.Resource is Endpoint
            ? PermissionScope.Platform()
            : null;
    }

    private static bool TryGetGuidRouteValue(
        HttpContext context,
        string key,
        out Guid value)
    {
        var raw =
            context.Request.RouteValues[key];

        return Guid.TryParse(
            raw?.ToString(),
            out value);
    }
}
