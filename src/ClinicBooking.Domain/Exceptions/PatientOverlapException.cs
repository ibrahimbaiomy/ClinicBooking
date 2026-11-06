namespace ClinicBooking.Domain.Exceptions;

/// <summary>Another active appointment of the patient that overlaps the time being booked (D41, D66).</summary>
public sealed record OverlapMatch(
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    long DoctorId,
    string DoctorNameAr,
    string DoctorNameEn,
    long ClinicId,
    string ClinicNameAr,
    string ClinicNameEn);

/// <summary>
/// The patient already has an active appointment overlapping this time (HTTP 409
/// <c>error.appointment.patient_overlap</c>, D41, D66): a warning the user may override by resending with
/// <c>confirmPatientOverlap</c>, the D63 mechanism. <see cref="MatchCount"/> counts every overlap;
/// <see cref="Matches"/> holds only those in clinics where the caller holds appointments.read. The message
/// is the key only.
/// </summary>
public sealed class PatientOverlapException : DomainException
{
    public const string Key = "error.appointment.patient_overlap";

    public PatientOverlapException(int matchCount, IReadOnlyList<OverlapMatch> matches)
        : base(Key)
    {
        MatchCount = matchCount;
        Matches = matches;
    }

    public int MatchCount { get; }

    public IReadOnlyList<OverlapMatch> Matches { get; }
}
