using ClinicBooking.Domain.ValueObjects;

namespace ClinicBooking.Domain.Entities;

/// <summary>
/// Reference data with an Arabic and an English name. The two normalised columns are set
/// together with the names (<see cref="SetNames"/>), so they can never be forgotten (D49).
/// </summary>
public class Specialty : SoftDeletableEntity
{
    public const int NameMaxLength = 100;

    // For EF Core.
    protected Specialty()
    {
    }

    public string NameAr { get; private set; } = string.Empty;

    public string NameEn { get; private set; } = string.Empty;

    /// <summary>Search and uniqueness key of <see cref="NameAr"/> (<see cref="SearchText.Normalize"/>).</summary>
    public string NameArNormalized { get; private set; } = string.Empty;

    public string NameEnNormalized { get; private set; } = string.Empty;

    public static Specialty Create(string nameAr, string nameEn)
    {
        var specialty = new Specialty();
        specialty.SetNames(nameAr, nameEn);
        return specialty;
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
