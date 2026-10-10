using Common.Application.Extensions;
using Common.Application.Options;
using Common.Infrastructure.Extensions;
using Common.Infrastructure.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace Identity.Infrastructure.RateLimiting;

public static class IdentityRateLimitingPolicies
{
    public static IEnumerable<
        Action<RateLimiterOptions, CustomRateLimitingOptions>> Policies =>
    [
        (options, limits) =>
            options.AddPolicy(
                "Sms",
                context => RateLimitPartitions.FixedWindow(
                    context,
                    "Sms",
                    GetPartitionKey(context),
                    limits.Sms)),

        (options, limits) =>
            options.AddPolicy(
                "OtpVerify",
                context => RateLimitPartitions.FixedWindow(
                    context,
                    "OtpVerify",
                    GetPartitionKey(context),
                    limits.OtpVerify)),

        (options, limits) =>
            options.AddPolicy(
                "TokenRefresh",
                context => RateLimitPartitions.FixedWindow(
                    context,
                    "TokenRefresh",
                    GetPartitionKey(context),
                    limits.TokenRefresh))
    ];

    private static string GetPartitionKey(HttpContext context)
    {
        // GetIpAddress() must rely on correctly configured trusted
        // forwarded headers when the application runs behind a proxy.
        return context.GetIpAddress()
               ?? context.Connection.RemoteIpAddress?.ToString()
               ?? "unknown";
    }
}
