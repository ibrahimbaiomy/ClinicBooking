using ClinicBooking.Domain.ValueObjects;

namespace ClinicBooking.Domain.Entities;

/// <summary>
/// A doctor (D32, D61): two names with their normalised copies (D49, no uniqueness: people share
/// names), one or more specialties, assignments to clinics, and a slot-duration history (D43).
/// Soft-deletable (D35). The related rows are never removed through a cascade: a soft delete keeps them.
/// </summary>
public class Doctor : SoftDeletableEntity, IBilingualName
{
    public const int NameMaxLength = 100;

    // For EF Core.
    protected Doctor()
    {
    }

    public string NameAr { get; private set; } = string.Empty;

    public string NameEn { get; private set; } = string.Empty;

    /// <summary>Search and sort key of <see cref="NameAr"/> (<see cref="SearchText.Normalize"/>).</summary>
    public string NameArNormalized { get; private set; } = string.Empty;

    public string NameEnNormalized { get; private set; } = string.Empty;

    public ICollection<DoctorSpecialty> Specialties { get; } = new List<DoctorSpecialty>();

    /// <summary>Every assignment, active or not. Rows are never deleted (D61).</summary>
    public ICollection<DoctorClinic> Clinics { get; } = new List<DoctorClinic>();

    public ICollection<DoctorSlotDuration> SlotDurations { get; } = new List<DoctorSlotDuration>();

    public static Doctor Create(string nameAr, string nameEn)
    {
        var doctor = new Doctor();
        doctor.SetNames(nameAr, nameEn);
        return doctor;
    }

    /// <summary>Stores the names as entered (trimmed) and recomputes the normalised copies.</summary>
    public void SetNames(string nameAr, string nameEn)
    {
        NameAr = nameAr.Trim();
        NameEn = nameEn.Trim();
        NameArNormalized = SearchText.Normalize(NameAr);
        NameEnNormalized = SearchText.Normalize(NameEn);
    }
}
