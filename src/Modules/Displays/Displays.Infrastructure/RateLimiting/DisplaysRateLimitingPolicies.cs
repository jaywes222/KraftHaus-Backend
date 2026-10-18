using Common.Application.Options;
using Common.Infrastructure.Extensions;
using Common.Infrastructure.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace Displays.Infrastructure.RateLimiting;

/// <summary>
///     Spec 08 §11: the anonymous pairing endpoints, per IP. Asking for a code uses the waitlist limits
///     (<c>CustomRateLimitingOptions:Waitlist</c>); polling, which a screen does every few seconds, the scan limits.
/// </summary>
public static class DisplaysRateLimitingPolicies
{
    public const string PairingStart = "DisplayPairingStart";

    public const string PairingPoll = "DisplayPairingPoll";

    public static IEnumerable<Action<RateLimiterOptions, CustomRateLimitingOptions>> Policies =>
    [
        (options, limits) =>
            options.AddPolicy(
                PairingStart,
                context => RateLimitPartitions.FixedWindow(context, PairingStart, PartitionKey(context), limits.Waitlist)),

        (options, limits) =>
            options.AddPolicy(
                PairingPoll,
                context => RateLimitPartitions.FixedWindow(context, PairingPoll, PartitionKey(context), limits.Scan))
    ];

    private static string PartitionKey(HttpContext context) =>
        context.GetIpAddress()
        ?? context.Connection.RemoteIpAddress?.ToString()
        ?? "unknown";
}
