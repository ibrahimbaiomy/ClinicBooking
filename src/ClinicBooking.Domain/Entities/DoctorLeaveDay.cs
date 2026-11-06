namespace ClinicBooking.Domain.Entities;

/// <summary>
/// A whole Cairo calendar day on which a doctor works nowhere (D41, D66): one person, every clinic. Removing
/// leave hard-deletes the row, like the other schedule rows (D35).
/// </summary>
public class DoctorLeaveDay : AuditableEntity
{
    // For EF Core.
    protected DoctorLeaveDay()
    {
    }

    public long DoctorId { get; private set; }

    public DateOnly Date { get; private set; }

    public static DoctorLeaveDay Create(long doctorId, DateOnly date) => new() { DoctorId = doctorId, Date = date };
}
