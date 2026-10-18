using Common.Application.EventCatalog;
using Ticketing.Application.Waitlist;
using Xunit;

namespace Ticketing.Tests;

public sealed class WaitlistPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void OpenWhilePublishedAndBeforeTheFirstDateStarts()
    {
        Assert.True(WaitlistPolicy.IsOpen(Snapshot(EventLifecycle.Published, Now.AddDays(27)), Now));
    }

    [Fact]
    public void ClosedOnceTheFirstDateHasStarted()
    {
        Assert.False(WaitlistPolicy.IsOpen(Snapshot(EventLifecycle.Published, Now), Now));
        Assert.False(WaitlistPolicy.IsOpen(Snapshot(EventLifecycle.Published, Now.AddMinutes(-1)), Now));
    }

    [Theory]
    [InlineData(EventLifecycle.Draft)]
    [InlineData(EventLifecycle.Cancelled)]
    [InlineData(EventLifecycle.Completed)]
    public void ClosedUnlessPublished(EventLifecycle status)
    {
        Assert.False(WaitlistPolicy.IsOpen(Snapshot(status, Now.AddDays(27)), Now));
    }

    [Fact]
    public void ClosedWithoutAScheduledDate()
    {
        Assert.False(WaitlistPolicy.IsOpen(Snapshot(EventLifecycle.Published, null), Now));
    }

    internal static EventSnapshot Snapshot(EventLifecycle status, DateTimeOffset? firstStartsAt) =>
        new(Guid.CreateVersion7(), "krafthaus-001", "KraftHaus 001", status, firstStartsAt, firstStartsAt?.AddHours(8));
}
