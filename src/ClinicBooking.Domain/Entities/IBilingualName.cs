namespace ClinicBooking.Domain.Entities;

/// <summary>
/// An entity with an Arabic and an English display name and their normalised copies (D37, D49):
/// Specialties, Clinics and Doctors. Lets the list search and ordering be written once.
/// </summary>
public interface IBilingualName
{
    string NameAr { get; }

    string NameEn { get; }

    /// <summary>Search and sort key of <see cref="NameAr"/> (<c>SearchText.Normalize</c>).</summary>
    string NameArNormalized { get; }

    string NameEnNormalized { get; }
}
