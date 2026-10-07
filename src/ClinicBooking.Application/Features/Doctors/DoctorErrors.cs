using ClinicBooking.Domain.Entities;
using ClinicBooking.Domain.ValueObjects;

namespace ClinicBooking.Application.Features.Doctors;

/// <summary>Error keys of the Doctors feature (D10, D61).</summary>
public static class DoctorErrors
{
    public const string NotFound = "error.doctor.not_found";
    public const string SpecialtiesRequired = "error.doctor.specialties_required";
    public const string SpecialtiesTooMany = "error.doctor.specialties_too_many";
    public const string SpecialtyUnavailable = "error.doctor.specialty_unavailable";
    public const string ClinicsRequired = "error.doctor.clinics_required";
    public const string ClinicsTooMany = "error.doctor.clinics_too_many";
    public const string SlotMinutesRequired = "error.doctor.slot_minutes_required";
    public const string SlotMinutesInvalid = DoctorSlotDuration.MinutesInvalidKey;
    public const string EffectiveFromRequired = "error.doctor.effective_from_required";
    public const string EffectiveFromNotFuture = "error.doctor.effective_from_not_future";
    public const string IsActiveRequiresClinic = "error.doctor.is_active_requires_clinic";
    public const string ClinicAlreadyAssigned = "error.doctor.clinic_already_assigned";
    public const string ClinicNotAssigned = "error.doctor.clinic_not_assigned";
    public const string AllClinicsRequired = "error.doctor.all_clinics_required";
    public const string PeriodsRequired = "error.doctor.periods_required";
    public const string PeriodsTooMany = "error.doctor.periods_too_many";
    public const string PeriodDayInvalid = WeeklyPeriod.DayInvalidKey;
    public const string PeriodStartRequired = "error.doctor.period_start_required";
    public const string PeriodEndRequired = "error.doctor.period_end_required";
    public const string PeriodTimeInvalid = WeeklyPeriod.TimeInvalidKey;
    public const string PeriodEndNotAfterStart = WeeklyPeriod.EndNotAfterStartKey;
    public const string PeriodsOverlap = WeeklyPeriod.OverlapKey;
    public const string PeriodOverlapsOtherClinic = WeeklyPeriod.OverlapsOtherClinicKey;
    public const string PeriodShorterThanSlot = WeeklyPeriod.ShorterThanSlotKey;
}
