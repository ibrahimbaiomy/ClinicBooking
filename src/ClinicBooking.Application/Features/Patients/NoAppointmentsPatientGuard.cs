using ClinicBooking.Application.Interfaces;

namespace ClinicBooking.Application.Features.Patients;

/// <summary>Phase 1 (D63): no appointment exists, so deleting a patient hurts nothing.</summary>
public sealed class NoAppointmentsPatientGuard : IPatientScheduleGuard
{
    public Task EnsureCanDeleteAsync(long patientId, CancellationToken cancellationToken) => Task.CompletedTask;
}
