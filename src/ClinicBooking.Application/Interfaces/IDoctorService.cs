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
}
