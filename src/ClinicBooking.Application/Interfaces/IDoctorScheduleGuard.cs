using ClinicBooking.Domain.ValueObjects;

namespace ClinicBooking.Application.Interfaces;

/// <summary>
/// The Phase 2 seam (D61): rules that depend on appointments, which do not exist yet. Phase 1 registers an
/// implementation that allows everything; Appointments replaces it, and each method then throws a keyed
/// <c>DomainException</c> when the change would hurt a future appointment. Listed in STATUS.md.
/// </summary>
public interface IDoctorScheduleGuard
{
    /// <summary>Deleting a doctor with upcoming appointments needs confirmation and cancels them (D35).</summary>
    Task EnsureCanDeleteAsync(long doctorId, CancellationToken cancellationToken);

    /// <summary>Deactivating an assignment that has future appointments in that clinic (D61).</summary>
    Task EnsureCanDeactivateAsync(long doctorId, long clinicId, CancellationToken cancellationToken);

    /// <summary>A new slot duration takes effect only after the doctor's last active appointment (D43).</summary>
    Task EnsureSlotChangeAllowedAsync(long doctorId, DateOnly effectiveFrom, CancellationToken cancellationToken);

    /// <summary>New working hours must not leave a future appointment outside its period or off the grid (D43).</summary>
    Task EnsureWorkingHoursChangeAllowedAsync(
        long doctorId,
        long clinicId,
        IReadOnlyList<WeeklyPeriod> periods,
        CancellationToken cancellationToken);
}
