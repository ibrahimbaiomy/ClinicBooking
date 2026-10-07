namespace ClinicBooking.Domain.Entities;

/// <summary>
/// A doctor's assignment to a clinic (D32, D61). Never deleted: deactivating means the doctor stopped
/// working there, reactivating that they returned, and a wrong assignment is deactivated. The weekly
/// working hours of the pair hang off it; they are kept while it is inactive.
/// </summary>
public class DoctorClinic : AuditableEntity
{
    public long DoctorId { get; set; }

    public long ClinicId { get; set; }

    public bool IsActive { get; set; }

    public Clinic Clinic { get; set; } = null!;

    public ICollection<WorkingHourPeriod> WorkingHours { get; } = new List<WorkingHourPeriod>();
}
