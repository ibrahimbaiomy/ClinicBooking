using ClinicBooking.Domain.Exceptions;
using ClinicBooking.Domain.ValueObjects;

namespace ClinicBooking.Domain.Entities;

/// <summary>
/// Reference data with an Arabic and an English name, like <see cref="Specialty"/> (D49): the two
/// normalised columns are set together with the names (<see cref="SetNames"/>). The address and the
/// phone number are optional contact details (<see cref="SetContact"/>, D55).
/// </summary>
public class Clinic : SoftDeletableEntity, IBilingualName
{
    public const int NameMaxLength = 100;
    public const int AddressMaxLength = 300;

    public const string PhoneInvalidKey = "error.clinic.phone_invalid";

    // For EF Core.
    protected Clinic()
    {
    }

    public string NameAr { get; private set; } = string.Empty;

    public string NameEn { get; private set; } = string.Empty;

    /// <summary>Search and uniqueness key of <see cref="NameAr"/> (<see cref="SearchText.Normalize"/>).</summary>
    public string NameArNormalized { get; private set; } = string.Empty;

    public string NameEnNormalized { get; private set; } = string.Empty;

    /// <summary>Free text as entered (trimmed); null when absent.</summary>
    public string? Address { get; private set; }

    /// <summary>E.164 (<see cref="PhoneNumber"/>); null when absent.</summary>
    public string? Phone { get; private set; }

    public static Clinic Create(string nameAr, string nameEn, string? address, string? phone)
    {
        var clinic = new Clinic();
        clinic.SetNames(nameAr, nameEn);
        clinic.SetContact(address, phone);
        return clinic;
    }

    /// <summary>Stores the names as entered (trimmed) and recomputes the normalised copies.</summary>
    public void SetNames(string nameAr, string nameEn)
    {
        NameAr = nameAr.Trim();
        NameEn = nameEn.Trim();
        NameArNormalized = SearchText.Normalize(NameAr);
        NameEnNormalized = SearchText.Normalize(NameEn);
    }

    /// <summary>
    /// Replaces both contact details (blank means absent). An invalid phone number throws
    /// <see cref="InvalidRequestException"/> (<see cref="PhoneInvalidKey"/>), so a value that got past the
    /// validator is a 400 and never a 500.
    /// </summary>
    public void SetContact(string? address, string? phone)
    {
        var trimmed = string.IsNullOrWhiteSpace(address) ? null : address.Trim();
        if (trimmed is { Length: > AddressMaxLength })
        {
            throw new InvalidRequestException("error.clinic.address_too_long");
        }

        Phone = PhoneNumber.Normalize(phone, PhoneInvalidKey);
        Address = trimmed;
    }
}
