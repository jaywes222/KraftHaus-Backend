using System.Diagnostics;
using System.Security.Claims;
using Common.Application.Auditing;
using Common.Application.Auth;
using Common.Application.Tenancy;
using Microsoft.AspNetCore.Http;

namespace Common.Infrastructure.Auditing;

/// <summary>
///     Builds <see cref="AuditRequestContext" /> from the current HTTP request.
///     <list type="bullet">
///         <item>Actor: the authenticated principal (<c>sub</c>, <c>sid</c>), otherwise anonymous; system outside a request.</item>
///         <item>Tenant: the tenant resolved by the Tenancy module, falling back to the token's <c>tid</c> claim.</item>
///         <item>Correlation: the W3C trace id of <see cref="Activity.Current" /> plus <c>HttpContext.TraceIdentifier</c>.</item>
///     </list>
///     Deliberately does not capture IP addresses or user agents: they are personal data and the trace id already
///     links an audit entry to the request logs, which have their own (shorter) retention.
/// </summary>
internal sealed class HttpAuditRequestContextProvider(
    IHttpContextAccessor httpContextAccessor,
    ICurrentUser currentUser,
    ICurrentTenant? currentTenant = null)
    : IAuditRequestContextProvider
{
    private const string TenantClaim = "tid";

    public AuditRequestContext GetCurrent()
    {
        var traceId = CurrentTraceId();
        var httpContext = httpContextAccessor.HttpContext;

        if (httpContext is null)
        {
            return AuditRequestContext.System(traceId);
        }

        var actor = currentUser.Id.IsEmpty
            ? AuditActor.Anonymous
            : AuditActor.User(
                currentUser.Id.Value.ToString(),
                currentUser.SessionId);

        return new AuditRequestContext(
            actor,
            currentTenant is { IsResolved: true }
                ? currentTenant.TenantId.Value
                : TenantFromClaims(httpContext.User),
            traceId,
            httpContext.TraceIdentifier);
    }

    private static string? CurrentTraceId()
    {
        var activity = Activity.Current;

        return activity is null || activity.TraceId == default
            ? null
            : activity.TraceId.ToHexString();
    }

    private static Guid? TenantFromClaims(ClaimsPrincipal? principal)
    {
        var value = principal?.FindFirstValue(TenantClaim);

        return Guid.TryParse(value, out var tenantId)
            ? tenantId
            : null;
    }
}
