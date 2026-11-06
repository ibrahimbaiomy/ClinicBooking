namespace ClinicBooking.Application.Features.Common;

/// <summary>
/// Cairo wall-clock time, used only to validate rules (D12): "today" for slot-duration dates (D43), the Cairo
/// date of an appointment, and the instant a Cairo wall-clock time names (D66). The runtime image carries
/// tzdata, so the IANA id resolves on Linux and on Windows. Egypt observes daylight saving time, switching at
/// midnight: in spring a local hour does not exist, in autumn one occurs twice.
/// </summary>
public static class CairoTime
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

    public static DateOnly Today(TimeProvider clock) => DateOf(clock.GetUtcNow());

    /// <summary>The Cairo calendar date of an instant.</summary>
    public static DateOnly DateOf(DateTimeOffset instant)
    {
        var local = TimeZoneInfo.ConvertTime(instant, Zone);
        return new DateOnly(local.Year, local.Month, local.Day);
    }

    /// <summary>
    /// The UTC instant of a Cairo wall-clock time; null when daylight saving skips it; the earlier instant when
    /// the clock goes back and the time occurs twice, so a wall-clock time never names two instants (D66).
    /// </summary>
    public static DateTimeOffset? ToUtc(DateOnly date, TimeOnly time)
    {
        // The wall-clock reading written as if it were UTC; subtracting an offset gives a candidate instant.
        var wall = new DateTimeOffset(date.Year, date.Month, date.Day, time.Hour, time.Minute, time.Second, TimeSpan.Zero);
        var offsets = new[] { Zone.GetUtcOffset(wall.AddDays(-1)), Zone.GetUtcOffset(wall), Zone.GetUtcOffset(wall.AddDays(1)) };

        DateTimeOffset? earliest = null;
        foreach (var offset in offsets.Distinct())
        {
            var candidate = wall - offset;
            var back = TimeZoneInfo.ConvertTime(candidate, Zone);
            if (back.Year == date.Year && back.Month == date.Month && back.Day == date.Day
                && back.Hour == time.Hour && back.Minute == time.Minute && back.Second == time.Second
                && (earliest is null || candidate < earliest))
            {
                earliest = candidate;
            }
        }

        return earliest;
    }

    /// <summary>The first instant of a Cairo calendar day (later than midnight when daylight saving skips it).</summary>
    public static DateTimeOffset StartOfDay(DateOnly date)
    {
        for (var minute = 0; minute < 24 * 60; minute++)
        {
            if (ToUtc(date, TimeOnly.MinValue.AddMinutes(minute)) is { } instant)
            {
                return instant;
            }
        }

        throw new InvalidOperationException("A Cairo day without a valid minute.");
    }
}
