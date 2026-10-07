using ClinicBooking.Domain.Exceptions;

namespace ClinicBooking.Domain.ValueObjects;

/// <summary>
/// One working period on one weekday, in Cairo wall-clock time (D12, D32). Never crosses midnight:
/// <see cref="Start"/> is before <see cref="End"/> on the same day. Breaks are the gaps between periods.
/// </summary>
public readonly record struct WeeklyPeriod
{
    public const string DayInvalidKey = "error.doctor.period_day_invalid";
    public const string TimeInvalidKey = "error.doctor.period_time_invalid";
    public const string EndNotAfterStartKey = "error.doctor.period_end_not_after_start";
    public const string OverlapKey = "error.doctor.periods_overlap";
    public const string OverlapsOtherClinicKey = "error.doctor.period_overlaps_other_clinic";
    public const string ShorterThanSlotKey = "error.doctor.period_shorter_than_slot";

    private WeeklyPeriod(DayOfWeek day, TimeOnly start, TimeOnly end)
    {
        Day = day;
        Start = start;
        End = end;
    }

    public DayOfWeek Day { get; }

    public TimeOnly Start { get; }

    public TimeOnly End { get; }

    public TimeSpan Length => End - Start;

    /// <summary>
    /// A weekday 0 (Sunday) to 6 (Saturday), whole minutes, start before end. Anything else is a 400 with a
    /// key (D55 pattern): the validator says the same first, this guards every other caller.
    /// </summary>
    public static WeeklyPeriod Create(int day, TimeOnly start, TimeOnly end)
    {
        if (day is < 0 or > 6)
        {
            throw new InvalidRequestException(DayInvalidKey);
        }

        if (!IsWholeMinute(start) || !IsWholeMinute(end))
        {
            throw new InvalidRequestException(TimeInvalidKey);
        }

        if (start >= end)
        {
            throw new InvalidRequestException(EndNotAfterStartKey);
        }

        return new WeeklyPeriod((DayOfWeek)day, start, end);
    }

    public static bool IsWholeMinute(TimeOnly time) => time.Second == 0 && time.Millisecond == 0 && time.Microsecond == 0 && time.Nanosecond == 0;

    /// <summary>Same weekday and sharing at least one minute; periods that only touch do not overlap.</summary>
    public bool Overlaps(WeeklyPeriod other) => Day == other.Day && Start < other.End && other.Start < End;

    /// <summary>Extension member naming the offending period: its index in the list that was checked (D61).</summary>
    public const string PeriodIndexDetail = "periodIndex";

    /// <summary>Extension member naming the other clinic a period overlaps (D61).</summary>
    public const string ConflictingClinicIdDetail = "conflictingClinicId";

    /// <summary>
    /// 422 <see cref="OverlapKey"/> when two periods of one week overlap; <see cref="PeriodIndexDetail"/> names
    /// the later of the first overlapping pair, in list order.
    /// </summary>
    public static void EnsureNoOverlap(IReadOnlyList<WeeklyPeriod> periods)
    {
        for (var later = 1; later < periods.Count; later++)
        {
            for (var earlier = 0; earlier < later; earlier++)
            {
                if (periods[later].Overlaps(periods[earlier]))
                {
                    throw new BusinessRuleException(OverlapKey, new Dictionary<string, long> { [PeriodIndexDetail] = later });
                }
            }
        }
    }

    /// <summary>
    /// 422 <see cref="OverlapsOtherClinicKey"/> when a period overlaps one of the doctor's other clinics (D32).
    /// The details name the first such period (<see cref="PeriodIndexDetail"/>, only when
    /// <paramref name="withPeriodIndex"/>: an index means something only for a list the caller sent) and the
    /// clinic it overlaps (<see cref="ConflictingClinicIdDetail"/>).
    /// </summary>
    public static void EnsureNoOverlapWithOtherClinics(
        IReadOnlyList<WeeklyPeriod> periods,
        IReadOnlyList<(WeeklyPeriod Period, long ClinicId)> otherClinics,
        bool withPeriodIndex = true)
    {
        for (var index = 0; index < periods.Count; index++)
        {
            foreach (var (other, clinicId) in otherClinics)
            {
                if (periods[index].Overlaps(other))
                {
                    var details = new Dictionary<string, long> { [ConflictingClinicIdDetail] = clinicId };
                    if (withPeriodIndex)
                    {
                        details[PeriodIndexDetail] = index;
                    }

                    throw new BusinessRuleException(OverlapsOtherClinicKey, details);
                }
            }
        }
    }

    /// <summary>
    /// 422 <see cref="ShorterThanSlotKey"/> when a period cannot hold one slot (D43);
    /// <see cref="PeriodIndexDetail"/> names the first such period.
    /// </summary>
    public static void EnsureEachHoldsASlot(IReadOnlyList<WeeklyPeriod> periods, int slotMinutes)
    {
        for (var index = 0; index < periods.Count; index++)
        {
            if (periods[index].Length < TimeSpan.FromMinutes(slotMinutes))
            {
                throw new BusinessRuleException(ShorterThanSlotKey, new Dictionary<string, long> { [PeriodIndexDetail] = index });
            }
        }
    }
}
