using ClinicBooking.Domain.Exceptions;

namespace ClinicBooking.Domain.Entities;

/// <summary>
/// One row of a doctor's slot-duration history (D43): from <see cref="EffectiveFrom"/> (a Cairo calendar
/// date) the doctor's slots last <see cref="SlotMinutes"/> in every clinic. Hard-deleted when a pending
/// change is replaced (D35, D61).
/// </summary>
public class DoctorSlotDuration : AuditableEntity
{
    public const int MinMinutes = 5;
    public const int MaxMinutes = 120;
    public const int StepMinutes = 5;

    public const string MinutesInvalidKey = "error.doctor.slot_minutes_invalid";

    // For EF Core.
    protected DoctorSlotDuration()
    {
    }

    public long DoctorId { get; private set; }

    public int SlotMinutes { get; private set; }

    public DateOnly EffectiveFrom { get; private set; }

    /// <summary>5 to 120 minutes in steps of 5; anything else is a 400 (D55 pattern).</summary>
    public static bool IsValidMinutes(int minutes) =>
        minutes is >= MinMinutes and <= MaxMinutes && minutes % StepMinutes == 0;

    public static DoctorSlotDuration Create(long doctorId, int slotMinutes, DateOnly effectiveFrom)
    {
        if (!IsValidMinutes(slotMinutes))
        {
            throw new InvalidRequestException(MinutesInvalidKey);
        }

        return new DoctorSlotDuration { DoctorId = doctorId, SlotMinutes = slotMinutes, EffectiveFrom = effectiveFrom };
    }

    /// <summary>For a doctor that is being created together with this row.</summary>
    public static DoctorSlotDuration CreateFor(Doctor doctor, int slotMinutes, DateOnly effectiveFrom)
    {
        var row = Create(0, slotMinutes, effectiveFrom);
        doctor.SlotDurations.Add(row);
        return row;
    }
}
