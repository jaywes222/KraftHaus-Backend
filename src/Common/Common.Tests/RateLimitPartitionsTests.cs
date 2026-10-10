using System.Threading.RateLimiting;
using Common.Application.Options;
using Common.Infrastructure.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Common.Tests;

public sealed class RateLimitPartitionsTests
{
    [Fact]
    public async Task FixedWindowWhenLimitIsReachedShouldRejectAdditionalRequests()
    {
        using var services = new ServiceCollection().BuildServiceProvider();

        using var limiter = CreateLimiter(
            services,
            limit: 2,
            partitionKey: _ => "192.0.2.10");

        using var first = await limiter.AcquireAsync(new DefaultHttpContext());
        using var second = await limiter.AcquireAsync(new DefaultHttpContext());
        using var third = await limiter.AcquireAsync(new DefaultHttpContext());

        Assert.True(first.IsAcquired);
        Assert.True(second.IsAcquired);
        Assert.False(third.IsAcquired);
    }

    [Fact]
    public async Task FixedWindowWhenPartitionKeysDifferShouldUseSeparateBuckets()
    {
        using var services = new ServiceCollection().BuildServiceProvider();

        var addresses = new Queue<string>(
        [
            "192.0.2.10",
            "192.0.2.10",
            "192.0.2.20"
        ]);

        using var limiter = CreateLimiter(
            services,
            limit: 1,
            partitionKey: _ => addresses.Dequeue());

        using var first = await limiter.AcquireAsync(new DefaultHttpContext());
        using var second = await limiter.AcquireAsync(new DefaultHttpContext());
        using var third = await limiter.AcquireAsync(new DefaultHttpContext());

        Assert.True(first.IsAcquired);
        Assert.False(second.IsAcquired);
        Assert.True(third.IsAcquired);
    }

    private static PartitionedRateLimiter<HttpContext> CreateLimiter(
        IServiceProvider services,
        int limit,
        Func<HttpContext, string> partitionKey)
    {
        return PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            context.RequestServices = services;

            return RateLimitPartitions.FixedWindow(
                context,
                policyName: "TestPolicy",
                partitionKey: partitionKey(context),
                options: new FixedWindow
                {
                    Limit = limit,
                    PeriodInMs = 60_000,
                    QueueLimit = 0
                });
        });
    }
}
