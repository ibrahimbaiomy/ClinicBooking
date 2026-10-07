using ClinicBooking.Application.Interfaces;
using ClinicBooking.Domain.ValueObjects;

namespace ClinicBooking.Application.Features.Doctors;

/// <summary>
/// Phase 1 (D61): no appointment exists, so nothing can be hurt and every change is allowed.
/// Appointments (Phase 2) replace this registration with the real checks.
/// </summary>
public sealed class NoAppointmentsScheduleGuard : IDoctorScheduleGuard
{
    public Task EnsureCanDeleteAsync(long doctorId, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task EnsureCanDeactivateAsync(long doctorId, long clinicId, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task EnsureSlotChangeAllowedAsync(long doctorId, DateOnly effectiveFrom, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task EnsureWorkingHoursChangeAllowedAsync(
        long doctorId,
        long clinicId,
        IReadOnlyList<WeeklyPeriod> periods,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
