using ClinicBooking.Domain.Entities;
using ClinicBooking.Domain.Enums;
using ClinicBooking.Domain.Exceptions;

namespace ClinicBooking.Application.Features.Appointments;

/// <summary>
/// The confirm-and-cancel flow shared by deactivating an assignment, deleting a doctor or a patient and adding
/// leave (D35, D66): booked appointments that have not started are counted; without the caller's
/// confirmation that is a 409 with the count and nothing changes; with it they are cancelled in the same
/// transaction. Appointments that have started stay booked, to be recorded.
/// </summary>
public static class FutureAppointments
{
    /// <summary>Booked and not started: the appointments a change would hurt.</summary>
    public static IQueryable<Appointment> Upcoming(this IQueryable<Appointment> appointments, DateTimeOffset now) =>
        appointments.Where(a => a.Status == AppointmentStatus.Booked && a.StartUtc > now);

    public static void CancelAfterConfirmation(
        IReadOnlyCollection<Appointment> upcoming,
        bool confirmed,
        string conflictKey,
        DateTimeOffset now)
    {
        if (upcoming.Count == 0)
        {
            return;
        }

        if (!confirmed)
        {
            throw new ConflictException(
                conflictKey,
                new Dictionary<string, long> { [AppointmentErrors.AppointmentCountDetail] = upcoming.Count });
        }

        foreach (var appointment in upcoming)
        {
            appointment.Cancel(now);
        }
    }
}
