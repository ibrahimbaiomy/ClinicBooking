using ClinicBooking.Application.Features.Common;
using ClinicBooking.Domain.ValueObjects;

namespace ClinicBooking.Application.Features.Appointments;

/// <summary>One slot of a doctor's day: one appointment occupies exactly one (D43).</summary>
public sealed record Slot(DateTimeOffset StartUtc, DateTimeOffset EndUtc);

/// <summary>
/// The slot grid of one Cairo date (D43, D66): in every working-hour period of that weekday, slots start at
/// <c>period start + n × duration</c> and end inside the period, in wall-clock time. A start that daylight
/// saving skips is no slot; a start that occurs twice counts once, at its first occurrence
/// (<see cref="CairoTime.ToUtc"/>).
/// </summary>
public static class SlotGrid
{
    public static IReadOnlyList<Slot> For(DateOnly date, IEnumerable<WeeklyPeriod> periods, int slotMinutes)
    {
        var slots = new List<Slot>();
        foreach (var period in periods.Where(p => p.Day == date.DayOfWeek))
        {
            var periodEnd = Minutes(period.End);
            for (var start = Minutes(period.Start); start + slotMinutes <= periodEnd; start += slotMinutes)
            {
                if (CairoTime.ToUtc(date, TimeOnly.MinValue.AddMinutes(start)) is { } startUtc)
                {
                    slots.Add(new Slot(startUtc, startUtc.AddMinutes(slotMinutes)));
                }
            }
        }

        return slots.OrderBy(s => s.StartUtc).ToList();
    }

    private static int Minutes(TimeOnly time) => (time.Hour * 60) + time.Minute;
}
