using ClinicBooking.Application.DTOs;

namespace ClinicBooking.Application.Interfaces;

public interface IClinicService
{
    Task<PagedResponse<ClinicResponse>> ListAsync(ListClinicsQuery query, CancellationToken cancellationToken);

    Task<ClinicResponse> GetAsync(long id, CancellationToken cancellationToken);

    Task<ClinicResponse> CreateAsync(CreateClinicRequest request, CancellationToken cancellationToken);

    Task<ClinicResponse> UpdateAsync(long id, UpdateClinicRequest request, CancellationToken cancellationToken);

    /// <summary>Soft delete (D35).</summary>
    Task DeleteAsync(long id, CancellationToken cancellationToken);
}
