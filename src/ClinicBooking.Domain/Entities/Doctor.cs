namespace ClinicBooking.Domain.Entities;

public class Doctor : BaseEntity
{
    public string FirstName { get; set; } = string.Empty;

    public string LastName { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }

    public string? Email { get; set; }

    public long SpecialtyId { get; set; }

    public Specialty Specialty { get; set; } = null!;

    public long ClinicId { get; set; }

    public Clinic Clinic { get; set; } = null!;

    public ICollection<DoctorWorkingHour> WorkingHours { get; set; } = new List<DoctorWorkingHour>();
}
