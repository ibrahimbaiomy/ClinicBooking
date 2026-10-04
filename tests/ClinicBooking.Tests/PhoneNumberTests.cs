using ClinicBooking.Domain.Entities;
using ClinicBooking.Domain.Exceptions;
using ClinicBooking.Domain.ValueObjects;

namespace ClinicBooking.Tests;

public class PhoneNumberTests
{
    // LRM, RLM and the bidi isolates Arabic text often carries along.
    private const string Lrm = "‎";
    private const string Rlm = "‏";
    private const string Fsi = "⁨";
    private const string Pdi = "⁩";

    public static TheoryData<string, string> Valid => new()
    {
        // Egyptian mobile
        { "01012345678", "+201012345678" },
        { "01112345678", "+201112345678" },
        { "01212345678", "+201212345678" },
        { "01512345678", "+201512345678" },
        { "010 1234 5678", "+201012345678" },
        { "010-1234-5678", "+201012345678" },
        { "  01012345678  ", "+201012345678" },
        // Egyptian landline
        { "02 2345 6789", "+20223456789" },
        { "0223456789", "+20223456789" },
        { "03 123 4567", "+2031234567" },
        { "(02) 2345-6789", "+20223456789" },
        { "0403123456", "+20403123456" },
        // International
        { "+201012345678", "+201012345678" },
        { "+20 10 1234 5678", "+201012345678" },
        { "0020 10 1234 5678", "+201012345678" },
        { "+1 (415) 555-2671", "+14155552671" },
        { "+44 20 7946 0958", "+442079460958" },
        { "+12345678", "+12345678" },
        { "+123456789012345", "+123456789012345" },
        // Arabic-Indic and Persian digits
        { "٠١٠١٢٣٤٥٦٧٨", "+201012345678" },
        { "+٢٠ ١٠ ١٢٣٤ ٥٦٧٨", "+201012345678" },
        { "۰۱۰۱۲۳۴۵۶۷۸", "+201012345678" },
        { "+۲۰۱۰۱۲۳۴۵۶۷۸", "+201012345678" },
        { "٠١٠ 1234 ۵۶۷۸", "+201012345678" },
        // Bidi marks and other invisible format characters
        { $"{Lrm}01012345678{Lrm}", "+201012345678" },
        { $"{Rlm}+20 10{Rlm} 1234 5678", "+201012345678" },
        { $"{Fsi}010 1234 5678{Pdi}", "+201012345678" },
        { $"{Lrm}٠١٠{Rlm}١٢٣٤٥٦٧٨", "+201012345678" },
        { "01012345678 ", "+201012345678" }
    };

    [Theory]
    [MemberData(nameof(Valid))]
    public void Valid_numbers_are_stored_as_E164(string input, string expected)
    {
        Assert.True(PhoneNumber.TryNormalize(input, out var normalized));
        Assert.Equal(expected, normalized);
        Assert.True(normalized!.Length <= PhoneNumber.MaxLength);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void A_blank_number_is_absent_not_invalid(string? input)
    {
        Assert.True(PhoneNumber.TryNormalize(input, out var normalized));
        Assert.Null(normalized);
        Assert.Null(PhoneNumber.Normalize(input, "error.test.phone"));
    }

    public static TheoryData<string> Invalid => new()
    {
        "abc",
        "0101234567a",
        "12345",                      // too short, no prefix
        "1012345678",                 // neither + nor a leading 0: ambiguous
        "+",
        "+0123456789",                // country codes do not start with 0
        "+1234567",                   // 7 digits
        "+1234567890123456",          // 16 digits
        "00",
        "0001234567890",              // 00 then a leading 0
        "010123456789",               // 12 digits
        "0101234567",                 // 10 digits starting 01
        "01312345678",                // 11 digits, 013 is not a mobile prefix
        "0112345678",                 // 10 digits, 011 is a mobile prefix: wrong length
        "0212345",                    // landline too short
        "02123456789012",             // landline too long
        "01",                         // too short
        "+20+1012345678",             // a second plus
        "010+12345678",               // plus in the middle
        "010 1234 5678 ext 5",
        "010/1234567",
        "16123",                      // short hotline numbers are not accepted yet (D55)
        "19123",
        "19019",
        "٠١٠١٢٣٤٥٦٧٨x",
        // Raw input longer than 32 characters is invalid before any parsing, even when it is
        // only separators around a valid number.
        "010 1234 5678                       ",
        "+20 10 1234 5678 -- -- -- -- -- --",
        "01012345678012345678901234567890123"
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void Invalid_numbers_are_rejected(string input)
    {
        // Raw length is checked first on purpose: an oversized field is never parsed, even when it
        // would trim down to a valid number.
        Assert.False(PhoneNumber.TryNormalize(input, out var normalized));
        Assert.Null(normalized);
    }

    [Fact]
    public void Raw_input_longer_than_32_characters_is_invalid_even_if_a_valid_number_is_inside()
    {
        var padded = "01012345678" + new string(' ', 22); // 33 characters

        Assert.Equal(PhoneNumber.MaxInputLength + 1, padded.Length);
        Assert.False(PhoneNumber.TryNormalize(padded, out _));
        Assert.True(PhoneNumber.TryNormalize(padded[..PhoneNumber.MaxInputLength], out var normalized));
        Assert.Equal("+201012345678", normalized);
    }

    [Fact]
    public void Normalize_throws_InvalidRequestException_with_the_key_for_an_invalid_number()
    {
        var failure = Assert.Throws<InvalidRequestException>(() => PhoneNumber.Normalize("abc", "error.test.phone"));

        Assert.Equal("error.test.phone", failure.ErrorKey);
    }

    // ---- the entity: a value that slips past the validator is a 400, never a 500 ----------------

    [Theory]
    [InlineData("abc")]
    [InlineData("12345")]
    [InlineData("+0123456789")]
    [InlineData("16123")]
    public void A_clinic_rejects_an_invalid_phone_with_InvalidRequestException(string phone)
    {
        var create = Assert.Throws<InvalidRequestException>(() => Clinic.Create("عيادة", "Clinic", null, phone));
        Assert.Equal("error.clinic.phone_invalid", create.ErrorKey);
        Assert.Equal(Clinic.PhoneInvalidKey, create.ErrorKey);

        var clinic = Clinic.Create("عيادة", "Clinic", "Street 1", "01012345678");
        var update = Assert.Throws<InvalidRequestException>(() => clinic.SetContact("Street 2", phone));
        Assert.Equal("error.clinic.phone_invalid", update.ErrorKey);
        // Nothing was half-applied.
        Assert.Equal("Street 1", clinic.Address);
        Assert.Equal("+201012345678", clinic.Phone);
    }

    [Fact]
    public void A_clinic_stores_trimmed_contact_details_and_clears_blank_ones()
    {
        var clinic = Clinic.Create("عيادة", "Clinic", "  12 Nile St.  ", " 010 1234 5678 ");

        Assert.Equal("12 Nile St.", clinic.Address);
        Assert.Equal("+201012345678", clinic.Phone);

        clinic.SetContact("   ", "");

        Assert.Null(clinic.Address);
        Assert.Null(clinic.Phone);
    }

    [Fact]
    public void A_clinic_rejects_an_address_over_the_limit_with_InvalidRequestException()
    {
        var failure = Assert.Throws<InvalidRequestException>(
            () => Clinic.Create("عيادة", "Clinic", new string('a', Clinic.AddressMaxLength + 1), null));

        Assert.Equal("error.clinic.address_too_long", failure.ErrorKey);
        Assert.NotNull(Clinic.Create("عيادة", "Clinic", new string('a', Clinic.AddressMaxLength), null).Address);
    }
}
