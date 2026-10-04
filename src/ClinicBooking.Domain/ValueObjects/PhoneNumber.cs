using System.Globalization;
using System.Text;
using ClinicBooking.Domain.Exceptions;

namespace ClinicBooking.Domain.ValueObjects;

/// <summary>
/// The one phone-number normalisation (D38, D55): any accepted input is stored as E.164
/// (<c>+</c> and 8 to 15 digits). It is shared by every entity that stores a phone number.
/// No package is used.
/// </summary>
/// <remarks>
/// Accepted: the international form (<c>+…</c> or <c>00…</c>, 8 to 15 digits, first digit not 0) and
/// Egyptian national numbers with a single leading 0: mobile <c>01[0125]</c> plus 8 digits (11 in total) and
/// landline <c>0[2-9]</c> plus 7 or 8 digits (9 or 10 in total), stored as <c>+20…</c>. Spaces, hyphens, dots,
/// parentheses and bidi marks are ignored, and Arabic-Indic and Persian digits are folded to 0-9.
/// Anything else is rejected, including a number with neither <c>+</c> nor a leading 0 (ambiguous) and short
/// hotline numbers such as 16xxx (a known limit, D55).
/// </remarks>
public static class PhoneNumber
{
    /// <summary><c>+</c> and 15 digits.</summary>
    public const int MaxLength = 16;

    /// <summary>Longest raw input considered at all; anything longer is invalid before parsing.</summary>
    public const int MaxInputLength = 32;

    private const int MinInternationalDigits = 8;
    private const int MaxInternationalDigits = 15;

    /// <summary>
    /// True when the input is absent (null or blank: <paramref name="normalized"/> is null) or a valid
    /// number (<paramref name="normalized"/> is its E.164 form); false when it is invalid.
    /// </summary>
    public static bool TryNormalize(string? value, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (value.Length > MaxInputLength)
        {
            return false;
        }

        if (!TryReadDigits(value, out var plus, out var digits))
        {
            return false;
        }

        if (plus)
        {
            return TryInternational(digits, out normalized);
        }

        if (digits.StartsWith("00", StringComparison.Ordinal))
        {
            return TryInternational(digits[2..], out normalized);
        }

        if (digits.Length > 0 && digits[0] == '0' && IsEgyptianNational(digits))
        {
            normalized = "+20" + digits[1..];
            return true;
        }

        return false;
    }

    /// <summary>
    /// The E.164 form, or null for an absent number. An invalid number throws
    /// <see cref="InvalidRequestException"/> with <paramref name="errorKey"/> (a 400), so a value that
    /// slips past a validator is never a 500.
    /// </summary>
    public static string? Normalize(string? value, string errorKey) =>
        TryNormalize(value, out var normalized) ? normalized : throw new InvalidRequestException(errorKey);

    private static bool TryReadDigits(string value, out bool plus, out string digits)
    {
        plus = false;
        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            var folded = character switch
            {
                >= '٠' and <= '٩' => (char)('0' + (character - '٠')), // Arabic-Indic digits
                >= '۰' and <= '۹' => (char)('0' + (character - '۰')), // Persian digits
                _ => character
            };

            if (folded is >= '0' and <= '9')
            {
                builder.Append(folded);
            }
            else if (folded == '+')
            {
                // Only as the very first significant character.
                if (plus || builder.Length > 0)
                {
                    digits = string.Empty;
                    return false;
                }

                plus = true;
            }
            else if (!IsIgnorable(folded))
            {
                digits = string.Empty;
                return false;
            }
        }

        digits = builder.ToString();
        return true;
    }

    // Separators people type, and the invisible bidi marks Arabic text brings along.
    private static bool IsIgnorable(char character) =>
        character is ' ' or '-' or '.' or '(' or ')'
        || char.IsWhiteSpace(character)
        || char.GetUnicodeCategory(character) == UnicodeCategory.Format;

    private static bool TryInternational(string digits, out string? normalized)
    {
        normalized = null;
        if (digits.Length is < MinInternationalDigits or > MaxInternationalDigits || digits[0] == '0')
        {
            return false;
        }

        normalized = "+" + digits;
        return true;
    }

    private static bool IsEgyptianNational(string digits)
    {
        // Mobile: 01[0125] + 8 digits.
        if (digits.Length == 11)
        {
            return digits[1] == '1' && digits[2] is '0' or '1' or '2' or '5';
        }

        // Landline: 0[2-9] + 7 or 8 digits.
        return digits.Length is 9 or 10 && digits[1] is >= '2' and <= '9';
    }
}
