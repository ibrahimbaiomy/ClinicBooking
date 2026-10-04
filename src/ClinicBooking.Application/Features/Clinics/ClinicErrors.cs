using ClinicBooking.Domain.Entities;

namespace ClinicBooking.Application.Features.Clinics;

/// <summary>Error keys of the Clinics feature (D10, D55).</summary>
public static class ClinicErrors
{
    public const string NotFound = "error.clinic.not_found";
    public const string NameArTaken = "error.clinic.name_ar_taken";
    public const string NameEnTaken = "error.clinic.name_en_taken";
    public const string AddressTooLong = "error.clinic.address_too_long";

    /// <summary>The key lives in Domain, where the entity itself reports a bad number.</summary>
    public const string PhoneInvalid = Clinic.PhoneInvalidKey;
}
