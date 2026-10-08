using System.Text;

namespace ClinicBooking.Domain.ValueObjects;

/// <summary>How a patient search term is read (D63).</summary>
public enum PatientSearchKind
{
    /// <summary>Every word must match the normalised name (D49).</summary>
    Name,

    /// <summary>The stored E.164 phone starts with <see cref="PatientSearchTerm.Phone"/>.</summary>
    PhonePrefix,

    /// <summary>The stored E.164 phone contains <see cref="PatientSearchTerm.Phone"/>.</summary>
    PhoneContains
}

/// <summary>One search box, two meanings (D63).</summary>
public sealed record PatientSearchTerm(PatientSearchKind Kind, string Phone)
{
    public const int MinPhoneDigits = 3;

    /// <summary>
    /// Ignoring spaces, hyphens and a leading <c>+</c>, a query of at least 3 digits only (Arabic-Indic and Persian
    /// digits folded) is a phone search, anything else a name search. A leading <c>+</c> or <c>00</c> means the
    /// international form and a leading <c>0</c> an Egyptian national number (<c>+20</c>): both are prefix matches
    /// on the stored E.164 value. Digits with neither are a contains match.
    /// </summary>
    public static PatientSearchTerm Parse(string? query)
    {
        var text = (query ?? string.Empty).Trim();
        var plus = text.StartsWith('+');
        var digits = new StringBuilder(text.Length);

        foreach (var character in plus ? text[1..] : text)
        {
            var folded = character switch
            {
                >= '٠' and <= '٩' => (char)('0' + (character - '٠')), // Arabic-Indic digits
                >= '۰' and <= '۹' => (char)('0' + (character - '۰')), // Persian digits
                _ => character
            };

            if (folded is >= '0' and <= '9')
            {
                digits.Append(folded);
            }
            else if (folded is not (' ' or '-'))
            {
                return new PatientSearchTerm(PatientSearchKind.Name, string.Empty);
            }
        }

        var number = digits.ToString();
        if (number.Length < MinPhoneDigits)
        {
            return new PatientSearchTerm(PatientSearchKind.Name, string.Empty);
        }

        if (plus)
        {
            return new PatientSearchTerm(PatientSearchKind.PhonePrefix, "+" + number);
        }

        if (number.StartsWith("00", StringComparison.Ordinal))
        {
            return new PatientSearchTerm(PatientSearchKind.PhonePrefix, "+" + number[2..]);
        }

        return number[0] == '0'
            ? new PatientSearchTerm(PatientSearchKind.PhonePrefix, "+20" + number[1..])
            : new PatientSearchTerm(PatientSearchKind.PhoneContains, number);
    }
}
