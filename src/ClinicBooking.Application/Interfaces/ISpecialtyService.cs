using ClinicBooking.Application.DTOs;

namespace ClinicBooking.Application.Interfaces;

public interface ISpecialtyService
{
    Task<PagedResponse<SpecialtyResponse>> ListAsync(ListSpecialtiesQuery query, CancellationToken cancellationToken);

    Task<SpecialtyResponse> GetAsync(long id, CancellationToken cancellationToken);

    Task<SpecialtyResponse> CreateAsync(CreateSpecialtyRequest request, CancellationToken cancellationToken);

    Task<SpecialtyResponse> UpdateAsync(long id, UpdateSpecialtyRequest request, CancellationToken cancellationToken);

    /// <summary>Soft delete (D35).</summary>
    Task DeleteAsync(long id, CancellationToken cancellationToken);
}
