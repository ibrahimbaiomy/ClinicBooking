namespace ClinicBooking.Domain.Entities;

/// <summary>One specialty of one doctor. A join table: replacing the specialties deletes rows (D35).</summary>
public class DoctorSpecialty : AuditableEntity
{
    public long DoctorId { get; set; }

    public long SpecialtyId { get; set; }

    public Specialty Specialty { get; set; } = null!;
}
