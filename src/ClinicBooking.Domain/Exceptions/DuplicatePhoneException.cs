namespace ClinicBooking.Domain.Exceptions;

/// <summary>A live patient that already has the phone number being saved (D44, D63).</summary>
public sealed record PhoneMatch(long Id, string Name, string Phone);

/// <summary>
/// Other live patients have this phone (HTTP 409 <c>error.patient.phone_exists</c>, D44, D63): a warning the
/// user may override by resending with <c>confirmDuplicatePhone</c>. The response carries
/// <see cref="MatchCount"/> and, only for a caller who may read patients, <see cref="Matches"/>. The exception
/// message is the key only: names and phones never reach a log (D38, D45). Phase 2 reuses this mechanism for
/// the patient-overlap warning (D41).
/// </summary>
public sealed class DuplicatePhoneException : DomainException
{
    public const string Key = "error.patient.phone_exists";

    public DuplicatePhoneException(int matchCount, IReadOnlyList<PhoneMatch> matches)
        : base(Key)
    {
        MatchCount = matchCount;
        Matches = matches;
    }

    public int MatchCount { get; }

    /// <summary>Up to five, by name; empty when the caller does not hold patients.read.</summary>
    public IReadOnlyList<PhoneMatch> Matches { get; }
}
