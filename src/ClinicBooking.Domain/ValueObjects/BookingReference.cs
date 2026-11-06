using System.Linq;
using System.Security.Cryptography;

namespace ClinicBooking.Domain.ValueObjects;

/// <summary>
/// The short reference an appointment is addressed by and that is read out to patients (D6, D41, D66): six
/// characters from an alphabet without the look-alikes 0/O and 1/I/L, cryptographically random. Unique: the
/// service checks before inserting and regenerates on a collision; a unique index is the guard.
/// </summary>
public static class BookingReference
{
    public const int Length = 6;

    /// <summary>2 to 9 and A to Z without I, L and O: 31 characters, about 887 million references.</summary>
    public const string Alphabet = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

    public static string New() => RandomNumberGenerator.GetString(Alphabet, Length);

    /// <summary>
    /// The stored form of a reference someone typed (trimmed, upper case), or null when it cannot be one, so a
    /// malformed reference simply finds nothing.
    /// </summary>
    public static string? Normalize(string? value)
    {
        var upper = value?.Trim().ToUpperInvariant();
        return upper is { Length: Length } && upper.All(c => Alphabet.Contains(c, StringComparison.Ordinal)) ? upper : null;
    }
}
