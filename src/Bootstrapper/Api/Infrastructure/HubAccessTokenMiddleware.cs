using Microsoft.Extensions.Primitives;

namespace Api.Infrastructure;

/// <summary>
///     Browsers cannot set an Authorization header on a WebSocket, so the SignalR client sends the access token as
///     <c>?access_token=</c>. For real-time hub paths only, this turns it into a bearer header before authentication runs
///     (Spec 08 §10). Nowhere else is a token accepted from the query string.
/// </summary>
internal sealed class HubAccessTokenMiddleware(RequestDelegate next)
{
    public const string HubPathPrefix = "/hubs";

    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Request.Path.StartsWithSegments(HubPathPrefix, StringComparison.OrdinalIgnoreCase) &&
            StringValues.IsNullOrEmpty(context.Request.Headers.Authorization) &&
            context.Request.Query.TryGetValue("access_token", out var token) &&
            !StringValues.IsNullOrEmpty(token))
        {
            context.Request.Headers.Authorization = $"Bearer {token}";
        }

        return next(context);
    }
}
