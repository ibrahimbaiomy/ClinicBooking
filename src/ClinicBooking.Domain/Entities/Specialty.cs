namespace ClinicBooking.Domain.Entities;

public class Specialty : BaseEntity
{
    public string Name { get; set; } = string.Empty;

    public ICollection<Doctor> Doctors { get; set; } = new List<Doctor>();
}
