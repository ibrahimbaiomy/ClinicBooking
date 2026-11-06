using ClinicBooking.Application.DTOs;

namespace ClinicBooking.Application.Interfaces;

/// <summary>Doctors, their clinic assignments, working hours, slot duration (D61) and leave days (D66).</summary>
public interface IDoctorService
{
    Task<PagedResponse<DoctorResponse>> ListAsync(ListDoctorsQuery query, CancellationToken cancellationToken);

    Task<DoctorResponse> GetAsync(long id, CancellationToken cancellationToken);

    /// <summary>Needs doctors.manage in every clinic of the request.</summary>
    Task<DoctorResponse> CreateAsync(CreateDoctorRequest request, CancellationToken cancellationToken);

    /// <summary>Names and specialties; needs doctors.manage in any of the doctor's clinics.</summary>
    Task<DoctorResponse> UpdateAsync(long id, UpdateDoctorRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Soft delete (D35); needs doctors.manage in all of the doctor's clinics. Upcoming appointments make a 409
    /// with their count unless <paramref name="confirmCancelAppointments"/>, which cancels them (D66).
    /// </summary>
    Task DeleteAsync(long id, bool confirmCancelAppointments, CancellationToken cancellationToken);

    /// <summary>Schedules a slot duration after today; needs doctors.manage in any of the doctor's clinics.</summary>
    Task<DoctorResponse> ChangeSlotDurationAsync(long id, ChangeSlotDurationRequest request, CancellationToken cancellationToken);

    /// <summary>A new, active assignment; the caller already holds doctors.manage in the clinic (policy).</summary>
    Task<DoctorResponse> AddClinicAsync(long id, long clinicId, CancellationToken cancellationToken);

    /// <summary>The doctor works there again; refused (422) when its stored hours overlap another active clinic.</summary>
    Task<DoctorResponse> ActivateClinicAsync(long id, long clinicId, CancellationToken cancellationToken);

    /// <summary>
    /// The doctor stopped working there; the hours are kept. Upcoming appointments there make a 409 unless the
    /// request confirms their cancellation (D66).
    /// </summary>
    Task<DoctorResponse> DeactivateClinicAsync(
        long id,
        long clinicId,
        DeactivateClinicRequest? request,
        CancellationToken cancellationToken);

    /// <summary>The week of one assignment, active or not. Open to any signed-in user.</summary>
    Task<WorkingHoursResponse> GetWorkingHoursAsync(long id, long clinicId, CancellationToken cancellationToken);

    /// <summary>Full weekly replace; the caller already holds doctors.manage in the clinic (policy).</summary>
    Task<WorkingHoursResponse> ReplaceWorkingHoursAsync(
        long id,
        long clinicId,
        ReplaceWorkingHoursRequest request,
        CancellationToken cancellationToken);

    /// <summary>The doctor's leave days in a range. Open to any signed-in user (D66).</summary>
    Task<LeaveDaysResponse> ListLeaveDaysAsync(long id, LeaveDaysQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Leave on a range of dates from today; needs doctors.manage in any of the doctor's clinics. Dates already on
    /// leave are skipped; upcoming appointments on the new dates make one 409 unless confirmed (D66).
    /// </summary>
    Task<LeaveDaysResponse> AddLeaveDaysAsync(long id, AddLeaveDaysRequest request, CancellationToken cancellationToken);

    /// <summary>Removes one leave day, today or later; needs doctors.manage in any of the doctor's clinics.</summary>
    Task RemoveLeaveDayAsync(long id, DateOnly date, CancellationToken cancellationToken);
}
