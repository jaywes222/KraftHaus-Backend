using System.Net;
using System.Threading.RateLimiting;
using Common.Application.Extensions;
using Common.Application.Options;
using Common.Infrastructure.Extensions;
using Common.Infrastructure.RateLimiting;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Api.Middlewares;

internal static class RateLimitingMiddleware
{
    public static IServiceCollection AddCustomRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration,
        params IEnumerable<Action<RateLimiterOptions, CustomRateLimitingOptions>>[] rateLimitingPoliciesPerModule)
    {
        return services
            .AddRateLimiter(opt =>
            {
                var customRateLimitingOptions =
                    GetCustomRateLimitingOptions(configuration);

                opt.OnRejected = WriteTooManyRequestsToResponse();

                opt.GlobalLimiter =
                    GlobalRateLimiter(customRateLimitingOptions);

                // Allow each module to register its rate-limit needs
                // in a decoupled way.
                foreach (var policy in rateLimitingPoliciesPerModule.SelectMany(x => x))
                {
                    policy(opt, customRateLimitingOptions);
                }
            });
    }

    private static CustomRateLimitingOptions GetCustomRateLimitingOptions(
        IConfiguration configuration)
    {
        return configuration
                   .GetSection(nameof(CustomRateLimitingOptions))
                   .Get<CustomRateLimitingOptions>()
               ?? throw new InvalidOperationException(
                   "Custom rate limiting options are null.");
    }

    private static PartitionedRateLimiter<HttpContext> GlobalRateLimiter(
        CustomRateLimitingOptions rateLimitingOptions)
    {
        return PartitionedRateLimiter.Create<HttpContext, string>(httpContext =>
        {
            var partitionKey =
                httpContext.GetIpAddress() ?? "N/A";

            if (IsExempt(
                    httpContext.Request.Path,
                    rateLimitingOptions.ExemptPathPrefixes))
            {
                return RateLimitPartition.GetNoLimiter(partitionKey);
            }

            var globalRateLimiting =
                rateLimitingOptions.Global
                ?? throw new InvalidOperationException(
                    "Global rate limiting is null.");

            return RateLimitPartitions.FixedWindow(
                httpContext,
                "Global",
                partitionKey,
                globalRateLimiting);
        });
    }

    private static bool IsExempt(
        PathString path,
        IReadOnlyList<string> exemptPathPrefixes)
    {
        for (var i = 0; i < exemptPathPrefixes.Count; i++)
        {
            if (path.StartsWithSegments(
                    exemptPathPrefixes[i],
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static Func<OnRejectedContext, CancellationToken, ValueTask>
        WriteTooManyRequestsToResponse()
    {
        return (context, _) =>
        {
            var httpContext = context.HttpContext;

            var env =
                httpContext.RequestServices
                    .GetRequiredService<IWebHostEnvironment>();

            var problemDetailsService =
                httpContext.RequestServices
                    .GetRequiredService<IProblemDetailsService>();

            var problemDetails = new ProblemDetails
            {
                Status = (int)HttpStatusCode.TooManyRequests,

                Title =
                    "Too many requests. Please try again later.",

                Instance =
                    $"{httpContext.Request.Method} " +
                    $"{httpContext.Request.Path.Value}"
            };

            if (context.Lease.TryGetMetadata(
                    MetadataName.RetryAfter,
                    out var retryAfter))
            {
                problemDetails.Detail =
                    $"Please retry after {retryAfter}.";
            }

            problemDetails.AddErrorKey(
                nameof(HttpStatusCode.TooManyRequests));

            problemDetails.AddErrors([]);

            problemDetails.Extensions.TryAdd(
                "traceId",
                httpContext.TraceIdentifier);

            problemDetails.Extensions.TryAdd(
                "environment",
                env.EnvironmentName);

            httpContext.Response.StatusCode =
                (int)HttpStatusCode.TooManyRequests;

            return problemDetailsService.WriteAsync(
                new ProblemDetailsContext
                {
                    HttpContext = httpContext,
                    ProblemDetails = problemDetails
                });
        };
    }
}
