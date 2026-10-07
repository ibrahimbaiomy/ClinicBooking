using ClinicBooking.Application.DTOs;

namespace ClinicBooking.Application.Interfaces;

/// <summary>Doctors, their clinic assignments, working hours and slot duration (D61).</summary>
public interface IDoctorService
{
    Task<PagedResponse<DoctorResponse>> ListAsync(ListDoctorsQuery query, CancellationToken cancellationToken);

    Task<DoctorResponse> GetAsync(long id, CancellationToken cancellationToken);

    /// <summary>Needs doctors.manage in every clinic of the request.</summary>
    Task<DoctorResponse> CreateAsync(CreateDoctorRequest request, CancellationToken cancellationToken);

    /// <summary>Names and specialties; needs doctors.manage in any of the doctor's clinics.</summary>
    Task<DoctorResponse> UpdateAsync(long id, UpdateDoctorRequest request, CancellationToken cancellationToken);

    /// <summary>Soft delete (D35); needs doctors.manage in all of the doctor's clinics.</summary>
    Task DeleteAsync(long id, CancellationToken cancellationToken);

    /// <summary>A new, active assignment; the caller already holds doctors.manage in the clinic (policy).</summary>
    Task<DoctorResponse> AddClinicAsync(long id, long clinicId, CancellationToken cancellationToken);

    /// <summary>The doctor works there again; refused (422) when its stored hours overlap another active clinic.</summary>
    Task<DoctorResponse> ActivateClinicAsync(long id, long clinicId, CancellationToken cancellationToken);

    /// <summary>The doctor stopped working there; the hours are kept.</summary>
    Task<DoctorResponse> DeactivateClinicAsync(long id, long clinicId, CancellationToken cancellationToken);

    /// <summary>The week of one assignment, active or not. Open to any signed-in user.</summary>
    Task<WorkingHoursResponse> GetWorkingHoursAsync(long id, long clinicId, CancellationToken cancellationToken);

    /// <summary>Full weekly replace; the caller already holds doctors.manage in the clinic (policy).</summary>
    Task<WorkingHoursResponse> ReplaceWorkingHoursAsync(
        long id,
        long clinicId,
        ReplaceWorkingHoursRequest request,
        CancellationToken cancellationToken);
}
