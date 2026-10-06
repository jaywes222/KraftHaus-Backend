using Common.Domain.TimeProvision;
using Xunit;

namespace Common.Tests.UnitTests;

public class NairobiTimeTests
{
    // Tiny fake so these tests need no extra package. (Microsoft.Extensions.Time.Testing has FakeTimeProvider.)
    private sealed class TestClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static DateTimeOffset Utc(int y, int m, int d, int h = 0, int min = 0)
        => new(y, m, d, h, min, 0, TimeSpan.Zero);

    [Fact]
    public void Late_evening_utc_is_the_next_day_in_nairobi()
    {
        // 21:30 UTC on 6 Nov is 00:30 on 7 Nov in Nairobi (event day)
        var instant = Utc(2026, 11, 6, 21, 30);
        Assert.Equal(new DateOnly(2026, 11, 7), instant.ToNairobiDate());
        Assert.Equal(TimeSpan.FromHours(3), instant.ToNairobi().Offset);
        Assert.Equal(instant, instant.ToNairobi()); // same instant
    }

    [Fact]
    public void Day_boundaries_are_half_open_in_utc()
    {
        var day = new DateOnly(2026, 11, 7);
        Assert.Equal(Utc(2026, 11, 6, 21), NairobiTime.StartOfDayUtc(day));
        Assert.Equal(Utc(2026, 11, 7, 21), NairobiTime.StartOfNextDayUtc(day));
    }

    [Fact]
    public void At_builds_a_utc_instant_from_nairobi_wall_clock()
        => Assert.Equal(Utc(2026, 11, 7, 15), NairobiTime.At(new DateOnly(2026, 11, 7), new TimeOnly(18, 0)));

    [Fact]
    public void Provider_helpers_read_the_injected_clock()
    {
        var clock = new TestClock(Utc(2026, 11, 6, 21, 30));
        Assert.Equal(new DateOnly(2026, 11, 7), clock.GetNairobiToday());
        Assert.Equal(TimeSpan.FromHours(3), clock.GetNairobiNow().Offset);
        Assert.Equal(Utc(2026, 11, 6, 21, 40), clock.FromNow(TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void Deadline_has_passed_at_the_exact_instant()
    {
        var deadline = Utc(2026, 11, 1, 12);
        var clock = new TestClock(deadline.AddTicks(-1));
        Assert.False(deadline.HasPassed(clock));
        clock.Now = deadline;
        Assert.True(deadline.HasPassed(clock));
    }

    [Fact]
    public void Zone_has_no_daylight_saving_and_a_fixed_offset()
    {
        Assert.False(NairobiTime.Zone.SupportsDaylightSavingTime);
        Assert.Equal(TimeSpan.FromHours(3), NairobiTime.Zone.BaseUtcOffset);
    }
}
