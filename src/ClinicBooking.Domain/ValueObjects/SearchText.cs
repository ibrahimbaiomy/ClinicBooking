using System.Globalization;
using System.Text;

namespace ClinicBooking.Domain.ValueObjects;

/// <summary>
/// The one normalisation used for search and for uniqueness of reference data (D37, D49).
/// No SQL Server collation folds the Arabic letter groups below, so the normalised text is
/// computed here and stored next to the display text; the same function normalises the
/// search term, so stored value and query cannot drift.
/// </summary>
public static class SearchText
{
    /// <summary>
    /// Unicode NFKC; removes Arabic diacritics, tatweel and invisible format characters;
    /// folds أ إ آ ٱ to ا, ة to ه, ى to ي and Arabic-Indic digits to 0-9; lower-cases;
    /// collapses whitespace and trims.
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = value.Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;

        foreach (var character in text)
        {
            if (IsIgnorable(character))
            {
                continue;
            }

            var mapped = Fold(character);
            if (char.IsWhiteSpace(mapped))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(char.ToLowerInvariant(mapped));
        }

        return builder.ToString();
    }

    private static bool IsIgnorable(char character) =>
        character == 'ـ'                                   // tatweel
        || (character >= 'ً' && character <= 'ٟ')     // harakat and other marks
        || character == 'ٰ'                                // superscript alef
        || char.GetUnicodeCategory(character) == UnicodeCategory.Format; // ZWJ, ZWNJ, LRM, RLM, BOM

    private static char Fold(char character) => character switch
    {
        'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
        'ة' => 'ه',
        'ى' => 'ي',
        >= '٠' and <= '٩' => (char)('0' + (character - '٠')), // Arabic-Indic digits
        >= '۰' and <= '۹' => (char)('0' + (character - '۰')), // Persian digits
        _ => character
    };
}
