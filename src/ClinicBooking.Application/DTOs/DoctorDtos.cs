namespace ClinicBooking.Application.DTOs;

public sealed record DoctorSpecialtyResponse(long Id, string NameAr, string NameEn);

/// <param name="IsActive">False when the doctor no longer works at this clinic (D61).</param>
public sealed record DoctorClinicResponse(long ClinicId, string NameAr, string NameEn, bool IsActive);

/// <param name="EffectiveFrom">A Cairo calendar date, after today (D43).</param>
public sealed record PendingSlotChangeResponse(int SlotMinutes, DateOnly EffectiveFrom);

/// <param name="Clinics">Every assignment in a live clinic, active or not.</param>
/// <param name="SlotMinutes">The slot duration in effect today (Cairo).</param>
/// <param name="PendingSlotChange">A scheduled change that has not taken effect yet, or null.</param>
/// <param name="RowVersion">Opaque concurrency token (base64). Send it back unchanged on edit.</param>
public sealed record DoctorResponse(
    long Id,
    string NameAr,
    string NameEn,
    IReadOnlyList<DoctorSpecialtyResponse> Specialties,
    IReadOnlyList<DoctorClinicResponse> Clinics,
    int SlotMinutes,
    PendingSlotChangeResponse? PendingSlotChange,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    string RowVersion);

/// <param name="SpecialtyIds">1 to 10 live specialties; duplicates are ignored.</param>
/// <param name="ClinicIds">1 to 20 clinics, each needing doctors.manage; duplicates are ignored.</param>
/// <param name="SlotMinutes">5 to 120 in steps of 5, in effect from today (Cairo).</param>
public sealed record CreateDoctorRequest(
    string? NameAr,
    string? NameEn,
    IReadOnlyList<long>? SpecialtyIds,
    IReadOnlyList<long>? ClinicIds,
    int? SlotMinutes);

/// <summary>A full replace of the names and the specialties (D55). Clinics have their own endpoints.</summary>
public sealed record UpdateDoctorRequest(string? NameAr, string? NameEn, IReadOnlyList<long>? SpecialtyIds, string? RowVersion);

/// <summary>Query string of the list endpoint (<see cref="NamedListQuery"/> plus filters).</summary>
public sealed class ListDoctorsQuery : NamedListQuery
{
    /// <summary>Only doctors with an assignment in this clinic.</summary>
    public long? ClinicId { get; set; }

    /// <summary>Only doctors with this specialty.</summary>
    public long? SpecialtyId { get; set; }

    /// <summary>With <see cref="ClinicId"/> only: whether that assignment is active.</summary>
    public bool? IsActive { get; set; }
}

/// <param name="DayOfWeek">.NET DayOfWeek: 0 = Sunday ... 6 = Saturday (D61).</param>
/// <param name="Start">Cairo wall-clock time, whole minutes.</param>
/// <param name="End">After <paramref name="Start"/> on the same day.</param>
public sealed record WorkingHourPeriodResponse(int DayOfWeek, TimeOnly Start, TimeOnly End);

/// <param name="IsActive">Whether the assignment is active; an inactive one keeps its hours (D61).</param>
/// <param name="RowVersion">The assignment's concurrency token: send it back with the replacement.</param>
public sealed record WorkingHoursResponse(
    long DoctorId,
    long ClinicId,
    bool IsActive,
    IReadOnlyList<WorkingHourPeriodResponse> Periods,
    string RowVersion);

/// <param name="DayOfWeek">0 = Sunday ... 6 = Saturday.</param>
public sealed record WorkingHourPeriodRequest(int? DayOfWeek, TimeOnly? Start, TimeOnly? End);

/// <summary>The whole week for one (doctor, clinic), a full replace; an empty list means no hours there.</summary>
/// <param name="Periods">At most 50.</param>
public sealed record ReplaceWorkingHoursRequest(IReadOnlyList<WorkingHourPeriodRequest>? Periods, string? RowVersion);

/// <summary>Schedules a new slot duration (D43). Replaces any change that has not taken effect yet.</summary>
/// <param name="SlotMinutes">5 to 120 in steps of 5.</param>
/// <param name="EffectiveFrom">A Cairo calendar date after today.</param>
public sealed record ChangeSlotDurationRequest(int? SlotMinutes, DateOnly? EffectiveFrom);
