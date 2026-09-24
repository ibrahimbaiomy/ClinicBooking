namespace ClinicBooking.Domain.Entities;

public class Clinic : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public string? Address { get; set; }

    public string? PhoneNumber { get; set; }

    public ICollection<Doctor> Doctors { get; set; } = new List<Doctor>();
}
