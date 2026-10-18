using Common.Application.Extensions;
using Common.Application.Options;
using Common.Infrastructure.Extensions;
using Common.Infrastructure.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace Commerce.Infrastructure.RateLimiting;

/// <summary>
///     Spec 08 §11: food checkout and payment requests send M-Pesa prompts, so they have their own policy (same limits as
///     ticket checkout, <c>CustomRateLimitingOptions:Checkout</c>), per signed-in customer; the pickup scan has its own.
/// </summary>
public static class CommerceRateLimitingPolicies
{
    public const string FoodCheckout = "FoodCheckout";

    public const string PickupScan = "PickupScan";

    public static IEnumerable<Action<RateLimiterOptions, CustomRateLimitingOptions>> Policies =>
    [
        (options, limits) =>
            options.AddPolicy(
                FoodCheckout,
                context => RateLimitPartitions.FixedWindow(
                    context,
                    FoodCheckout,
                    context.User.FindFirst("sub")?.Value ?? GetPartitionKey(context),
                    limits.Checkout)),

        (options, limits) =>
            options.AddPolicy(
                PickupScan,
                context => RateLimitPartitions.FixedWindow(
                    context,
                    PickupScan,
                    context.User.FindFirst("sub")?.Value ?? GetPartitionKey(context),
                    limits.Scan))
    ];

    private static string GetPartitionKey(HttpContext context) =>
        context.GetIpAddress()
        ?? context.Connection.RemoteIpAddress?.ToString()
        ?? "unknown";
}
