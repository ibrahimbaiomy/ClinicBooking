using ClinicBooking.Domain.Exceptions;
using ClinicBooking.Domain.ValueObjects;

namespace ClinicBooking.Tests;

/// <summary>The overlap and slot-length rules of working hours (D30: unit tests for real branching logic).</summary>
public class WeeklyPeriodTests
{
    private static WeeklyPeriod P(int day, string start, string end) =>
        WeeklyPeriod.Create(day, TimeOnly.Parse(start), TimeOnly.Parse(end));

    [Theory]
    [InlineData(1, "09:00", "13:00", 1, "12:59", "14:00", true)]
    [InlineData(1, "09:00", "13:00", 1, "13:00", "14:00", false)] // touching
    [InlineData(1, "09:00", "13:00", 1, "08:00", "09:00", false)] // touching
    [InlineData(1, "09:00", "13:00", 1, "10:00", "11:00", true)]  // inside
    [InlineData(1, "10:00", "11:00", 1, "09:00", "13:00", true)]  // around
    [InlineData(1, "09:00", "13:00", 2, "09:00", "13:00", false)] // another day
    public void Overlap_needs_the_same_day_and_a_shared_minute(
        int dayA, string startA, string endA, int dayB, string startB, string endB, bool expected)
    {
        var a = P(dayA, startA, endA);
        var b = P(dayB, startB, endB);

        Assert.Equal(expected, a.Overlaps(b));
        Assert.Equal(expected, b.Overlaps(a));
    }

    [Fact]
    public void A_week_with_two_overlapping_periods_is_refused_and_the_later_one_is_named()
    {
        var periods = new[] { P(0, "08:00", "09:00"), P(3, "09:00", "12:00"), P(5, "09:00", "10:00"), P(3, "11:00", "13:00") };

        var failure = Assert.Throws<BusinessRuleException>(() => WeeklyPeriod.EnsureNoOverlap(periods));

        Assert.Equal("error.doctor.periods_overlap", failure.ErrorKey);
        Assert.Equal(new Dictionary<string, long> { ["periodIndex"] = 3 }, failure.Details);
        WeeklyPeriod.EnsureNoOverlap(periods[..3]);
    }

    [Fact]
    public void Other_clinics_are_compared_period_by_period_and_the_clinic_is_named()
    {
        var mine = new[] { P(1, "08:00", "09:00"), P(0, "09:00", "12:00") };

        WeeklyPeriod.EnsureNoOverlapWithOtherClinics(mine, [(P(0, "12:00", "14:00"), 7), (P(1, "09:00", "12:00"), 8)]);
        var failure = Assert.Throws<BusinessRuleException>(
            () => WeeklyPeriod.EnsureNoOverlapWithOtherClinics(mine, [(P(1, "09:00", "12:00"), 7), (P(0, "11:00", "11:30"), 9)]));
        var withoutIndex = Assert.Throws<BusinessRuleException>(
            () => WeeklyPeriod.EnsureNoOverlapWithOtherClinics(mine, [(P(0, "11:00", "11:30"), 9)], withPeriodIndex: false));

        Assert.Equal("error.doctor.period_overlaps_other_clinic", failure.ErrorKey);
        Assert.Equal(new Dictionary<string, long> { ["conflictingClinicId"] = 9, ["periodIndex"] = 1 }, failure.Details);
        Assert.Equal(new Dictionary<string, long> { ["conflictingClinicId"] = 9 }, withoutIndex.Details);
    }

    [Fact]
    public void Each_period_must_hold_one_slot_and_the_first_short_one_is_named()
    {
        WeeklyPeriod.EnsureEachHoldsASlot([P(0, "09:00", "09:15")], 15);

        var failure = Assert.Throws<BusinessRuleException>(
            () => WeeklyPeriod.EnsureEachHoldsASlot([P(0, "09:00", "10:00"), P(1, "09:00", "09:14"), P(2, "09:00", "09:01")], 15));

        Assert.Equal("error.doctor.period_shorter_than_slot", failure.ErrorKey);
        Assert.Equal(new Dictionary<string, long> { ["periodIndex"] = 1 }, failure.Details);
    }

    [Theory]
    [InlineData(7, "09:00", "10:00", "error.doctor.period_day_invalid")]
    [InlineData(1, "09:00:01", "10:00", "error.doctor.period_time_invalid")]
    [InlineData(1, "10:00", "10:00", "error.doctor.period_end_not_after_start")]
    [InlineData(1, "23:00", "00:00", "error.doctor.period_end_not_after_start")]
    public void An_invalid_period_is_a_keyed_400(int day, string start, string end, string key)
    {
        var failure = Assert.Throws<InvalidRequestException>(() => P(day, start, end));

        Assert.Equal(key, failure.ErrorKey);
    }
}
