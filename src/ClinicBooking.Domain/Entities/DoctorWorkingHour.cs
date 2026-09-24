namespace ClinicBooking.Domain.Entities;

public class DoctorWorkingHour : BaseEntity
{
    public long DoctorId { get; set; }

    public Doctor Doctor { get; set; } = null!;

    public DayOfWeek DayOfWeek { get; set; }

    public TimeOnly StartTime { get; set; }

    public TimeOnly EndTime { get; set; }
}
