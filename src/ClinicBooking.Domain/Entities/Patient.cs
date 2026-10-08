using ClinicBooking.Domain.Exceptions;
using ClinicBooking.Domain.ValueObjects;

namespace ClinicBooking.Domain.Entities;

/// <summary>
/// A patient (D38, D44, D63): a name and a phone number, nothing else. Shared by every clinic. The name is
/// stored as entered (trimmed) with a normalised copy for search (D49); neither the name nor the phone is
/// unique (family members share numbers). Personal data: never logged and never put in an exception message.
/// </summary>
public class Patient : SoftDeletableEntity
{
    public const int NameMaxLength = 100;

    public const string NameRequiredKey = "error.patient.name_required";
    public const string NameTooLongKey = "error.patient.name_too_long";
    public const string PhoneRequiredKey = "error.patient.phone_required";
    public const string PhoneInvalidKey = "error.patient.phone_invalid";

    // For EF Core.
    protected Patient()
    {
    }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Search and sort key of <see cref="Name"/> (<see cref="SearchText.Normalize"/>).</summary>
    public string NameNormalized { get; private set; } = string.Empty;

    /// <summary>E.164 (<see cref="PhoneNumber"/>).</summary>
    public string Phone { get; private set; } = string.Empty;

    public static Patient Create(string name, string phone)
    {
        var patient = new Patient();
        patient.Set(name, phone);
        return patient;
    }

    /// <summary>
    /// Replaces both values (a full replace, D55). A missing, too long or invalid value throws
    /// <see cref="InvalidRequestException"/> with its key, so a value that got past the validator is a 400.
    /// </summary>
    public void Set(string name, string phone)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0)
        {
            throw new InvalidRequestException(NameRequiredKey);
        }

        if (trimmed.Length > NameMaxLength)
        {
            throw new InvalidRequestException(NameTooLongKey);
        }

        var normalizedPhone = PhoneNumber.Normalize(phone, PhoneInvalidKey)
                              ?? throw new InvalidRequestException(PhoneRequiredKey);

        Name = trimmed;
        NameNormalized = SearchText.Normalize(trimmed);
        Phone = normalizedPhone;
    }
}
