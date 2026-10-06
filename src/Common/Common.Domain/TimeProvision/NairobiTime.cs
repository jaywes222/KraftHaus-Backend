namespace Common.Domain.TimeProvision;

/// <summary>
/// Time helpers for a product whose "day" is a Nairobi day (event dates, check-in days, installment deadlines).
///
/// Rules:
/// 1. Read the clock only through <see cref="TimeProvider"/> (injected). Never <c>DateTime.Now</c> or <c>UtcNow</c>.
/// 2. Store and compare instants in UTC (<see cref="DateTimeOffset"/>). Convert to Nairobi only at the edge or to
///    decide which calendar day something falls on.
/// 3. Use half-open day ranges: [<see cref="StartOfDayUtc"/>, <see cref="StartOfNextDayUtc"/>).
///
/// Kenya is UTC+03:00 all year (no daylight saving), so a fixed offset is exact. It also avoids a platform trap:
/// Windows Server 2019 may not resolve the IANA id "Africa/Nairobi" (it needs ICU), and Linux does not know the
/// Windows id "E. Africa Standard Time".
/// </summary>
public static class NairobiTime
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(3);

    public static TimeZoneInfo Zone { get; } =
        TimeZoneInfo.CreateCustomTimeZone("EAT", Offset, "East Africa Time", "EAT");

    // ---- reading the clock ------------------------------------------------------------------

    /// <summary>The current instant with the Nairobi offset (+03:00). Same instant as <c>GetUtcNow()</c>.</summary>
    public static DateTimeOffset GetNairobiNow(this TimeProvider time) => time.GetUtcNow().ToOffset(Offset);

    /// <summary>Today's calendar date in Nairobi.</summary>
    public static DateOnly GetNairobiToday(this TimeProvider time) => time.GetUtcNow().ToNairobiDate();

    /// <summary>An instant at <paramref name="duration"/> from now, in UTC (hold expiry, OTP expiry, token expiry).</summary>
    public static DateTimeOffset FromNow(this TimeProvider time, TimeSpan duration)
        => time.GetUtcNow().Add(duration);

    /// <summary>True when <paramref name="deadline"/> is now or in the past.</summary>
    public static bool HasPassed(this DateTimeOffset deadline, TimeProvider time) => time.GetUtcNow() >= deadline;

    // ---- converting instants ----------------------------------------------------------------

    public static DateTimeOffset ToNairobi(this DateTimeOffset instant) => instant.ToOffset(Offset);

    /// <summary>Which Nairobi calendar day this instant falls on.</summary>
    public static DateOnly ToNairobiDate(this DateTimeOffset instant)
        => DateOnly.FromDateTime(instant.ToOffset(Offset).DateTime);

    // ---- building instants from Nairobi calendar values -------------------------------------

    /// <summary>The UTC instant at which a Nairobi day starts (00:00 +03:00).</summary>
    public static DateTimeOffset StartOfDayUtc(DateOnly nairobiDate) => At(nairobiDate, TimeOnly.MinValue);

    /// <summary>The UTC instant at which the next Nairobi day starts: an exclusive end for "end of day" deadlines.</summary>
    public static DateTimeOffset StartOfNextDayUtc(DateOnly nairobiDate) => StartOfDayUtc(nairobiDate.AddDays(1));

    /// <summary>A Nairobi wall-clock time on a Nairobi date, as a UTC instant.</summary>
    public static DateTimeOffset At(DateOnly nairobiDate, TimeOnly nairobiTime)
        => new DateTimeOffset(nairobiDate.ToDateTime(nairobiTime), Offset).ToUniversalTime();
}
