using ClinicBooking.Domain.Enums;
using ClinicBooking.Domain.Exceptions;

namespace ClinicBooking.Domain.Entities;

/// <summary>
/// One booked slot (D31, D41, D43, D66): a doctor, a clinic and a patient, from <see cref="StartUtc"/> to
/// <see cref="EndUtc"/> (the start plus the doctor's slot duration on that Cairo date, stored at booking).
/// Never deleted: a cancelled appointment keeps its row and frees its slot. Holds no free text. The foreign
/// keys are real (restrict) but there are no navigations, so a soft-deleted doctor or patient never hides an
/// appointment (D66).
/// </summary>
public class Appointment : AuditableEntity
{
    public const string NotBookedKey = "error.appointment.not_booked";
    public const string AlreadyStartedKey = "error.appointment.already_started";
    public const string NotStartedKey = "error.appointment.not_started";

    // For EF Core.
    protected Appointment()
    {
    }

    public string BookingReference { get; private set; } = string.Empty;

    public long DoctorId { get; private set; }

    public long ClinicId { get; private set; }

    public long PatientId { get; private set; }

    public DateTimeOffset StartUtc { get; private set; }

    public DateTimeOffset EndUtc { get; private set; }

    public AppointmentStatus Status { get; private set; }

    public static Appointment Book(
        string bookingReference,
        long doctorId,
        long clinicId,
        long patientId,
        DateTimeOffset startUtc,
        DateTimeOffset endUtc) =>
        new()
        {
            BookingReference = bookingReference,
            DoctorId = doctorId,
            ClinicId = clinicId,
            PatientId = patientId,
            StartUtc = startUtc,
            EndUtc = endUtc,
            Status = AppointmentStatus.Booked,
        };

    public bool HasStarted(DateTimeOffset now) => StartUtc <= now;

    /// <summary>
    /// 422 unless the appointment is booked and has not started: the condition to reschedule or cancel it
    /// (D66). The new time itself is checked by the service.
    /// </summary>
    public void EnsureBookedAndNotStarted(DateTimeOffset now)
    {
        EnsureBooked();
        if (HasStarted(now))
        {
            throw new BusinessRuleException(AlreadyStartedKey);
        }
    }

    public void Reschedule(DateTimeOffset startUtc, DateTimeOffset endUtc, DateTimeOffset now)
    {
        EnsureBookedAndNotStarted(now);
        StartUtc = startUtc;
        EndUtc = endUtc;
    }

    /// <summary>Only a booked appointment that has not started; frees the slot (D41).</summary>
    public void Cancel(DateTimeOffset now)
    {
        EnsureBookedAndNotStarted(now);
        Status = AppointmentStatus.Cancelled;
    }

    /// <summary>Completed or NoShow: only a booked appointment whose start has passed (D66).</summary>
    public void Record(AppointmentStatus outcome, DateTimeOffset now)
    {
        if (outcome is not (AppointmentStatus.Completed or AppointmentStatus.NoShow))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }

        EnsureBooked();
        if (!HasStarted(now))
        {
            throw new BusinessRuleException(NotStartedKey);
        }

        Status = outcome;
    }

    private void EnsureBooked()
    {
        if (Status != AppointmentStatus.Booked)
        {
            throw new BusinessRuleException(NotBookedKey);
        }
    }
}
