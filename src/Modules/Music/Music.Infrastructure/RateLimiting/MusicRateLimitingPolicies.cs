using Common.Application.Options;
using Common.Infrastructure.Extensions;
using Common.Infrastructure.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace Music.Infrastructure.RateLimiting;

/// <summary>
///     Song search (typed as you go) uses the scan limits per account; making a request the checkout limits. The
///     catalogue itself is protected separately by the one-call-a-second gate and the cache.
/// </summary>
public static class MusicRateLimitingPolicies
{
    public const string Search = "MusicSearch";

    public const string Request = "MusicRequest";

    public static IEnumerable<Action<RateLimiterOptions, CustomRateLimitingOptions>> Policies =>
    [
        (options, limits) =>
            options.AddPolicy(
                Search,
                context => RateLimitPartitions.FixedWindow(context, Search, PartitionKey(context), limits.Scan)),

        (options, limits) =>
            options.AddPolicy(
                Request,
                context => RateLimitPartitions.FixedWindow(context, Request, PartitionKey(context), limits.Checkout))
    ];

    private static string PartitionKey(HttpContext context) =>
        context.User.FindFirst("sub")?.Value
        ?? context.GetIpAddress()
        ?? context.Connection.RemoteIpAddress?.ToString()
        ?? "unknown";
}
