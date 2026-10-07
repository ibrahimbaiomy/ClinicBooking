using ClinicBooking.Domain.ValueObjects;

namespace ClinicBooking.Domain.Entities;

/// <summary>
/// One stored period of a (doctor, clinic) week (D12, D32): Cairo <see cref="TimeOnly"/> plus weekday,
/// never an absolute time. The week is saved as a full replace, so rows are hard-deleted.
/// </summary>
public class WorkingHourPeriod : AuditableEntity
{
    // For EF Core.
    protected WorkingHourPeriod()
    {
    }

    public long DoctorClinicId { get; private set; }

    public DayOfWeek DayOfWeek { get; private set; }

    public TimeOnly Start { get; private set; }

    public TimeOnly End { get; private set; }

    public static WorkingHourPeriod Create(WeeklyPeriod period) =>
        new() { DayOfWeek = period.Day, Start = period.Start, End = period.End };

    public WeeklyPeriod ToWeeklyPeriod() => WeeklyPeriod.Create((int)DayOfWeek, Start, End);
}
