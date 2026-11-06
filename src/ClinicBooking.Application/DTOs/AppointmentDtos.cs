namespace ClinicBooking.Application.DTOs;

/// <summary>Books one slot in the clinic of the route (D66).</summary>
/// <param name="DoctorId">A doctor active in that clinic.</param>
/// <param name="PatientId">A patient that is not deleted.</param>
/// <param name="StartUtc">A start on the doctor's grid, in the future, at most 90 days ahead (Cairo dates).</param>
/// <param name="ConfirmPatientOverlap">True to book although the patient has an overlapping appointment (D41).</param>
public sealed record BookAppointmentRequest(long? DoctorId, long? PatientId, DateTimeOffset? StartUtc, bool? ConfirmPatientOverlap);

/// <summary>A new start with the same doctor and clinic; every booking rule applies (D66).</summary>
/// <param name="RowVersion">The appointment's concurrency token.</param>
public sealed record RescheduleAppointmentRequest(DateTimeOffset? StartUtc, string? RowVersion, bool? ConfirmPatientOverlap);

/// <summary>Cancel, complete or no-show: only the appointment's concurrency token (D66).</summary>
public sealed record ChangeAppointmentStatusRequest(string? RowVersion);

/// <summary>Query string of a clinic's day list (D66).</summary>
public sealed class DayAppointmentsQuery
{
    /// <summary>A Cairo calendar date.</summary>
    public DateOnly? Date { get; set; }

    /// <summary>Only this doctor's appointments.</summary>
    public long? DoctorId { get; set; }
}

/// <summary>Query string of the free slots (D66).</summary>
public sealed class AvailableSlotsQuery
{
    /// <summary>A Cairo calendar date.</summary>
    public DateOnly? Date { get; set; }
}

/// <param name="IsDeleted">The doctor was deleted after this appointment was made (D66).</param>
public sealed record AppointmentDoctorResponse(long Id, string NameAr, string NameEn, bool IsDeleted);

public sealed record AppointmentClinicResponse(long Id, string NameAr, string NameEn);

/// <param name="Name">Null for a caller without patients.read.</param>
/// <param name="Phone">E.164; null for a caller without patients.read.</param>
/// <param name="IsDeleted">The patient was deleted after this appointment was made (D66).</param>
public sealed record AppointmentPatientResponse(long Id, string? Name, string? Phone, bool IsDeleted);

/// <param name="BookingReference">Six characters; the appointment's address in the API (D6).</param>
/// <param name="Status"><c>booked</c>, <c>completed</c>, <c>cancelled</c> or <c>noShow</c>.</param>
/// <param name="RowVersion">Opaque concurrency token (base64). Send it back to reschedule, cancel or record.</param>
public sealed record AppointmentResponse(
    string BookingReference,
    string Status,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc,
    AppointmentDoctorResponse Doctor,
    AppointmentClinicResponse Clinic,
    AppointmentPatientResponse Patient,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    string RowVersion);

public sealed record SlotResponse(DateTimeOffset StartUtc, DateTimeOffset EndUtc);

/// <param name="SlotMinutes">The doctor's slot duration on that date.</param>
/// <param name="Slots">Free slots in time order; empty on a leave day, an inactive assignment, a past date or one
/// past the booking limit.</param>
public sealed record AvailableSlotsResponse(long DoctorId, long ClinicId, DateOnly Date, int SlotMinutes, IReadOnlyList<SlotResponse> Slots);
