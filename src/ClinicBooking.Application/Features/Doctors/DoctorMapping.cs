using System.Linq.Expressions;
using ClinicBooking.Application.DTOs;
using ClinicBooking.Application.Features.Common;
using ClinicBooking.Domain.Entities;

namespace ClinicBooking.Application.Features.Doctors;

/// <summary>
/// The one hand-written mapping (D8). Queries project with <see cref="ToRowExpression"/> (no entity is
/// materialised); <see cref="ToResponse"/> then picks today's slot duration and the pending change from the
/// short history in memory, because "today" is a Cairo date (D12, D43).
/// </summary>
public static class DoctorMapping
{
    public sealed record SlotRow(int SlotMinutes, DateOnly EffectiveFrom);

    public sealed record DoctorRow(
        long Id,
        string NameAr,
        string NameEn,
        List<DoctorSpecialtyResponse> Specialties,
        List<DoctorClinicResponse> Clinics,
        List<SlotRow> Slots,
        DateTimeOffset CreatedAt,
        DateTimeOffset? UpdatedAt,
        byte[] RowVersion);

    // Soft-deleted clinics are ignored everywhere (D57); a specialty in use cannot be deleted (D50).
    public static readonly Expression<Func<Doctor, DoctorRow>> ToRowExpression = d =>
        new DoctorRow(
            d.Id,
            d.NameAr,
            d.NameEn,
            d.Specialties
                .OrderBy(s => s.Specialty.NameEnNormalized)
                .Select(s => new DoctorSpecialtyResponse(s.SpecialtyId, s.Specialty.NameAr, s.Specialty.NameEn))
                .ToList(),
            d.Clinics
                .Where(c => !c.Clinic.IsDeleted)
                .OrderBy(c => c.Clinic.NameEnNormalized)
                .Select(c => new DoctorClinicResponse(c.ClinicId, c.Clinic.NameAr, c.Clinic.NameEn, c.IsActive))
                .ToList(),
            d.SlotDurations.Select(s => new SlotRow(s.SlotMinutes, s.EffectiveFrom)).ToList(),
            d.CreatedAt,
            d.UpdatedAt,
            d.RowVersion);

    public static DoctorResponse ToResponse(this DoctorRow row, DateOnly today)
    {
        var current = row.Slots.Where(s => s.EffectiveFrom <= today).MaxBy(s => s.EffectiveFrom);
        var pending = row.Slots.Where(s => s.EffectiveFrom > today).MinBy(s => s.EffectiveFrom);

        return new DoctorResponse(
            row.Id,
            row.NameAr,
            row.NameEn,
            row.Specialties,
            row.Clinics,
            current?.SlotMinutes ?? 0,
            pending is null ? null : new PendingSlotChangeResponse(pending.SlotMinutes, pending.EffectiveFrom),
            row.CreatedAt,
            row.UpdatedAt,
            RowVersionCodec.Encode(row.RowVersion));
    }
}
