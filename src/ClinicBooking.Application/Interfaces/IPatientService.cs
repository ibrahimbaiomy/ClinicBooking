using ClinicBooking.Application.DTOs;

namespace ClinicBooking.Application.Interfaces;

/// <summary>Patients (D44, D63). Every method needs its global patients.* permission (the controller says so).</summary>
public interface IPatientService
{
    Task<PagedResponse<PatientResponse>> ListAsync(ListPatientsQuery query, CancellationToken cancellationToken);

    Task<PatientResponse> GetAsync(long id, CancellationToken cancellationToken);

    /// <summary>409 error.patient.phone_exists when live patients share the phone, unless confirmed.</summary>
    Task<PatientResponse> CreateAsync(CreatePatientRequest request, CancellationToken cancellationToken);

    /// <summary>Full replace; the duplicate-phone warning applies only when the phone changes.</summary>
    Task<PatientResponse> UpdateAsync(long id, UpdatePatientRequest request, CancellationToken cancellationToken);

    /// <summary>Soft delete (D35).</summary>
    Task DeleteAsync(long id, CancellationToken cancellationToken);
}
