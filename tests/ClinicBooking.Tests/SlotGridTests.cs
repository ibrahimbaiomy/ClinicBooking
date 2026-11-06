using ClinicBooking.Application.Features.Appointments;
using ClinicBooking.Application.Features.Common;
using ClinicBooking.Domain.ValueObjects;

namespace ClinicBooking.Tests;

/// <summary>
/// The slot grid, Cairo wall-clock conversion and booking references (D30: unit tests only for logic with real
/// branching; D43, D66). Egypt's clock in 2026: summer time (+3) from Friday 24 April 00:00 (the hour 00:00–00:59
/// does not exist) to Thursday 29 October 24:00 (23:00–23:59 occurs twice).
/// </summary>
public class SlotGridTests
{
    private static WeeklyPeriod P(DayOfWeek day, int startHour, int startMinute, int endHour, int endMinute) =>
        WeeklyPeriod.Create((int)day, new TimeOnly(startHour, startMinute), new TimeOnly(endHour, endMinute));

    private static string[] Local(IEnumerable<Slot> slots) =>
        slots.Select(s => TimeZoneInfo.ConvertTime(s.StartUtc, TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo")).ToString("HH:mm")).ToArray();

    [Fact]
    public void Slots_start_at_the_period_start_plus_n_durations_and_end_inside_the_period()
    {
        // Thursday 2 July 2026; two periods, the second not aligned with the first.
        var date = new DateOnly(2026, 7, 2);
        var slots = SlotGrid.For(date, [P(DayOfWeek.Thursday, 9, 0, 10, 10), P(DayOfWeek.Thursday, 10, 15, 11, 0)], 20);

        Assert.Equal(["09:00", "09:20", "09:40", "10:15", "10:35"], Local(slots));
        Assert.All(slots, s => Assert.Equal(TimeSpan.FromMinutes(20), s.EndUtc - s.StartUtc));
    }

    [Fact]
    public void Periods_of_other_weekdays_are_ignored_and_the_result_is_in_time_order()
    {
        var date = new DateOnly(2026, 7, 2); // Thursday
        var slots = SlotGrid.For(
            date,
            [P(DayOfWeek.Thursday, 14, 0, 14, 30), P(DayOfWeek.Friday, 9, 0, 12, 0), P(DayOfWeek.Thursday, 8, 0, 8, 30)],
            30);

        Assert.Equal(["08:00", "14:00"], Local(slots));
    }

    [Fact]
    public void A_period_shorter_than_a_slot_gives_no_slot()
    {
        Assert.Empty(SlotGrid.For(new DateOnly(2026, 7, 2), [P(DayOfWeek.Thursday, 9, 0, 9, 15)], 20));
    }

    [Fact]
    public void In_summer_Cairo_is_three_hours_ahead_and_in_winter_two()
    {
        Assert.Equal(new DateTimeOffset(2026, 7, 2, 6, 0, 0, TimeSpan.Zero), CairoTime.ToUtc(new DateOnly(2026, 7, 2), new TimeOnly(9, 0)));
        Assert.Equal(new DateTimeOffset(2026, 12, 2, 7, 0, 0, TimeSpan.Zero), CairoTime.ToUtc(new DateOnly(2026, 12, 2), new TimeOnly(9, 0)));
    }

    [Fact]
    public void A_time_skipped_in_spring_is_no_slot()
    {
        var date = new DateOnly(2026, 4, 24); // Friday: 00:00 jumps to 01:00
        Assert.Null(CairoTime.ToUtc(date, new TimeOnly(0, 30)));

        var slots = SlotGrid.For(date, [P(DayOfWeek.Friday, 0, 0, 2, 0)], 20);

        Assert.Equal(["01:00", "01:20", "01:40"], Local(slots));
        Assert.Equal(new DateTimeOffset(2026, 4, 23, 22, 0, 0, TimeSpan.Zero), slots[0].StartUtc);
    }

    [Fact]
    public void A_time_repeated_in_autumn_is_one_slot_at_its_first_occurrence()
    {
        var date = new DateOnly(2026, 10, 29); // Thursday: 24:00 goes back to 23:00
        var slots = SlotGrid.For(date, [P(DayOfWeek.Thursday, 22, 0, 23, 59)], 20);

        // 22:00, 22:20, 22:40 (+3), then 23:00 and 23:20 only once each, at +3.
        Assert.Equal(
            [
                new DateTimeOffset(2026, 10, 29, 19, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 10, 29, 19, 20, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 10, 29, 19, 40, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 10, 29, 20, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 10, 29, 20, 20, 0, TimeSpan.Zero),
            ],
            slots.Select(s => s.StartUtc));
        Assert.Equal(new DateOnly(2026, 10, 29), CairoTime.DateOf(new DateTimeOffset(2026, 10, 29, 21, 30, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void A_Cairo_day_starts_at_local_midnight_or_at_the_end_of_the_spring_gap()
    {
        Assert.Equal(new DateTimeOffset(2026, 7, 1, 21, 0, 0, TimeSpan.Zero), CairoTime.StartOfDay(new DateOnly(2026, 7, 2)));
        Assert.Equal(new DateTimeOffset(2026, 4, 23, 22, 0, 0, TimeSpan.Zero), CairoTime.StartOfDay(new DateOnly(2026, 4, 24)));
        Assert.Equal(new DateTimeOffset(2026, 10, 29, 22, 0, 0, TimeSpan.Zero), CairoTime.StartOfDay(new DateOnly(2026, 10, 30)));
    }

    [Fact]
    public void Around_midnight_the_Cairo_date_is_not_the_UTC_date()
    {
        Assert.Equal(new DateOnly(2026, 7, 2), CairoTime.DateOf(new DateTimeOffset(2026, 7, 1, 21, 0, 0, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2026, 7, 1), CairoTime.DateOf(new DateTimeOffset(2026, 7, 1, 20, 59, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Booking_references_use_six_unambiguous_characters()
    {
        var references = Enumerable.Range(0, 2000).Select(_ => BookingReference.New()).ToList();

        Assert.All(references, r =>
        {
            Assert.Equal(6, r.Length);
            Assert.DoesNotContain(r, c => "01OIL".Contains(c));
            Assert.Equal(r, BookingReference.Normalize(r));
        });
        Assert.True(references.Distinct().Count() > 1990); // random, not a sequence
    }

    [Theory]
    [InlineData(" a7k2m9 ", "A7K2M9")]
    [InlineData("A7K2M", null)]
    [InlineData("A7K2M0", null)]
    [InlineData("A7K2MO", null)]
    [InlineData("A7K2ML", null)]
    [InlineData(null, null)]
    public void A_typed_reference_is_trimmed_and_upper_cased_or_rejected(string? typed, string? expected)
    {
        Assert.Equal(expected, BookingReference.Normalize(typed));
    }
}
