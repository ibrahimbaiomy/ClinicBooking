using ClinicBooking.Application.Interfaces;
using ClinicBooking.Domain.Entities;

namespace ClinicBooking.Application.Features.Appointments;

/// <summary>Reads of a doctor's schedule shared by booking and the doctor's own rules (D43, D66).</summary>
public static class DoctorSchedule
{
    /// <summary>The slot duration in effect on a Cairo date: the latest history row starting on or before it.</summary>
    public static Task<int?> SlotMinutesOnAsync(this IAppDbContext db, long doctorId, DateOnly date, CancellationToken cancellationToken) =>
        db.DoctorSlotDurations
            .Where(s => s.DoctorId == doctorId && s.EffectiveFrom <= date)
            .OrderByDescending(s => s.EffectiveFrom)
            .Select(s => (int?)s.SlotMinutes)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>The same question over a history already loaded.</summary>
    public static int? SlotMinutesOn(IEnumerable<DoctorSlotDuration> history, DateOnly date) =>
        history.Where(s => s.EffectiveFrom <= date).OrderByDescending(s => s.EffectiveFrom).Select(s => (int?)s.SlotMinutes).FirstOrDefault();
}
