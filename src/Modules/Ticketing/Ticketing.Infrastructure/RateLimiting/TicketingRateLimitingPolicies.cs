using Common.Application.Extensions;
using Common.Application.Options;
using Common.Infrastructure.Extensions;
using Common.Infrastructure.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace Ticketing.Infrastructure.RateLimiting;

/// <summary>Spec 08 §11: the waitlist and checkout have their own rate-limit policies.</summary>
public static class TicketingRateLimitingPolicies
{
    public const string Waitlist = "Waitlist";

    public const string Checkout = "Checkout";

    public const string Scan = "Scan";

    public static IEnumerable<Action<RateLimiterOptions, CustomRateLimitingOptions>> Policies =>
    [
        (options, limits) =>
            options.AddPolicy(
                Waitlist,
                context => RateLimitPartitions.FixedWindow(
                    context,
                    Waitlist,
                    GetPartitionKey(context),
                    limits.Waitlist)),

        // Per signed-in customer (falls back to the IP): each accepted request can send an M-Pesa prompt.
        (options, limits) =>
            options.AddPolicy(
                Checkout,
                context => RateLimitPartitions.FixedWindow(
                    context,
                    Checkout,
                    context.User.FindFirst("sub")?.Value ?? GetPartitionKey(context),
                    limits.Checkout)),

        // Per door-staff account: a burst at the gate is normal, a script hammering codes is not.
        (options, limits) =>
            options.AddPolicy(
                Scan,
                context => RateLimitPartitions.FixedWindow(
                    context,
                    Scan,
                    context.User.FindFirst("sub")?.Value ?? GetPartitionKey(context),
                    limits.Scan))
    ];

    // Relies on the trusted forwarded-headers setup when the API runs behind IIS / a proxy.
    private static string GetPartitionKey(HttpContext context) =>
        context.GetIpAddress()
        ?? context.Connection.RemoteIpAddress?.ToString()
        ?? "unknown";
}
