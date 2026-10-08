namespace ClinicBooking.Application.Interfaces;

/// <summary>
/// The Phase 2 seam for patients (D63): deleting a patient with future appointments. Phase 1 registers an
/// implementation that allows everything; Appointments replaces it. Listed in STATUS.md.
/// </summary>
public interface IPatientScheduleGuard
{
    Task EnsureCanDeleteAsync(long patientId, CancellationToken cancellationToken);
}
