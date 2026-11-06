using ClinicBooking.Domain.Entities;
using ClinicBooking.Domain.Exceptions;

namespace ClinicBooking.Application.Features.Appointments;

/// <summary>Error keys of the Appointments feature (D10, D66).</summary>
public static class AppointmentErrors
{
    public const string NotFound = "error.appointment.not_found";

    // Input (400).
    public const string DoctorRequired = "error.appointment.doctor_required";
    public const string PatientRequired = "error.appointment.patient_required";
    public const string StartRequired = "error.appointment.start_required";
    public const string DateRequired = "error.appointment.date_required";
    public const string PatientUnavailable = "error.appointment.patient_unavailable";

    // Booking rules (422).
    public const string DoctorNotAvailable = "error.appointment.doctor_not_available";
    public const string InPast = "error.appointment.in_past";
    public const string TooFarAhead = "error.appointment.too_far_ahead";
    public const string OnLeave = "error.appointment.on_leave";
    public const string OffGrid = "error.appointment.off_grid";

    // Conflicts (409).
    public const string SlotTaken = "error.appointment.slot_taken";
    public const string PatientOverlap = PatientOverlapException.Key;

    // State (422).
    public const string NotBooked = Appointment.NotBookedKey;
    public const string AlreadyStarted = Appointment.AlreadyStartedKey;
    public const string NotStarted = Appointment.NotStartedKey;

    /// <summary>Extension member of a confirm-and-cancel 409 or a stranded-hours 422: how many appointments (D66).</summary>
    public const string AppointmentCountDetail = "appointmentCount";
}
