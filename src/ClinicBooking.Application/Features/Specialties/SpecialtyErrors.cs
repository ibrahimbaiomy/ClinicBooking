namespace ClinicBooking.Application.Features.Specialties;

/// <summary>Error keys of the Specialties feature (D10).</summary>
public static class SpecialtyErrors
{
    public const string NotFound = "error.specialty.not_found";
    public const string NameArTaken = "error.specialty.name_ar_taken";
    public const string NameEnTaken = "error.specialty.name_en_taken";
    public const string InUse = "error.specialty.in_use";
}
