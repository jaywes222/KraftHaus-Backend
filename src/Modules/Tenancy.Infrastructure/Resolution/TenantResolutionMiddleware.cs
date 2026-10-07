using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Tenancy.Application.Abstractions;

namespace Tenancy.Infrastructure.Resolution;

public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext httpContext, ITenantResolutionService resolver, TenantContext tenantContext)
    {
        if (httpContext.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
        {
            await next(httpContext);
            return;
        }
        var tidClaim = httpContext.User.FindFirstValue("tid");
        Guid? tid = null;
        if (tidClaim is not null)
        {
            if (!Guid.TryParse(tidClaim, out var parsedTid))
            {
                httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
                await httpContext.Response.WriteAsJsonAsync(new { code = "Tenant.InvalidTid", message = "The tid claim is not a valid GUID." }, httpContext.RequestAborted);
                return;
            }

            tid = parsedTid;
        }

        var result = await resolver.ResolveAsync(httpContext.Request.Host.Host, tid, httpContext.RequestAborted);
        if (result.IsFailure)
        {
            httpContext.Response.StatusCode = (int)result.Error!.StatusCode;
            await httpContext.Response.WriteAsJsonAsync(new { code = result.Error.Key, message = result.Error.Description }, httpContext.RequestAborted);
            return;
        }

        tenantContext.Set(result.Value!);
        await next(httpContext);
    }

}
