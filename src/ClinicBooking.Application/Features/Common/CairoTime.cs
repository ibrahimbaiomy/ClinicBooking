namespace ClinicBooking.Application.Features.Common;

/// <summary>
/// Cairo wall-clock time, used only to validate rules (D12): "today" for slot-duration dates (D43).
/// The runtime image carries tzdata, so the IANA id resolves on Linux and on Windows.
/// </summary>
public static class CairoTime
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

    public static DateOnly Today(TimeProvider clock)
    {
        var local = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Zone);
        return new DateOnly(local.Year, local.Month, local.Day);
    }
}
