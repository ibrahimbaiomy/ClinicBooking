using ClinicBooking.Domain.Entities;
using ClinicBooking.Domain.Exceptions;

namespace ClinicBooking.Application.Features.Patients;

/// <summary>Error keys of the Patients feature (D10, D63).</summary>
public static class PatientErrors
{
    public const string NotFound = "error.patient.not_found";
    public const string NameRequired = Patient.NameRequiredKey;
    public const string NameTooLong = Patient.NameTooLongKey;
    public const string NameInvalid = "error.patient.name_invalid";
    public const string PhoneRequired = Patient.PhoneRequiredKey;
    public const string PhoneInvalid = Patient.PhoneInvalidKey;
    public const string PhoneExists = DuplicatePhoneException.Key;
}
