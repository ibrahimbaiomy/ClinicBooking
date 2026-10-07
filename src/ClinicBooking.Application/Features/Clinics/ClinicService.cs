using ClinicBooking.Application.DTOs;
using ClinicBooking.Application.Features.Common;
using ClinicBooking.Application.Interfaces;
using ClinicBooking.Domain.Entities;
using ClinicBooking.Domain.Exceptions;

namespace ClinicBooking.Application.Features.Clinics;

/// <summary>Follows <c>SpecialtyService</c> step for step (D50, D55).</summary>
public sealed class ClinicService : IClinicService
{
    private readonly IAppDbContext _db;

    public ClinicService(IAppDbContext db)
    {
        _db = db;
    }

    // Address and phone are not searched (D55).
    public Task<PagedResponse<ClinicResponse>> ListAsync(ListClinicsQuery query, CancellationToken cancellationToken) =>
        _db.Clinics
            .MatchingEveryWord(query.Search)
            .ToPageAsync(query, ClinicMapping.ToResponseExpression, cancellationToken);

    public async Task<ClinicResponse> GetAsync(long id, CancellationToken cancellationToken)
    {
        return await _db.Clinics
                   .Where(c => c.Id == id)
                   .Select(ClinicMapping.ToResponseExpression)
                   .SingleOrDefaultAsync(cancellationToken)
               ?? throw new NotFoundException(ClinicErrors.NotFound);
    }

    public async Task<ClinicResponse> CreateAsync(CreateClinicRequest request, CancellationToken cancellationToken)
    {
        var clinic = Clinic.Create(request.NameAr!, request.NameEn!, request.Address, request.Phone);
        await EnsureNamesAreFreeAsync(clinic, excludedId: 0, cancellationToken);

        _db.Clinics.Add(clinic);
        await _db.SaveChangesAsync(cancellationToken);

        return clinic.ToResponse();
    }

    public async Task<ClinicResponse> UpdateAsync(long id, UpdateClinicRequest request, CancellationToken cancellationToken)
    {
        var clinic = await FindAsync(id, cancellationToken);
        ConcurrencyGuard.EnsureCurrent(clinic, request.RowVersion);

        clinic.SetNames(request.NameAr!, request.NameEn!);
        clinic.SetContact(request.Address, request.Phone);
        await EnsureNamesAreFreeAsync(clinic, clinic.Id, cancellationToken);

        await _db.SaveGuardedAsync(cancellationToken);

        return clinic.ToResponse();
    }

    public Task DeleteAsync(long id, CancellationToken cancellationToken) =>
        // Serializable: a doctor assigned here at the same moment either is seen here or fails (D61).
        _db.InSerializableTransactionAsync(
            async () =>
            {
                var clinic = await FindAsync(id, cancellationToken);

                // Any assignment of a live doctor, active or inactive, keeps the clinic (D55, D61).
                if (await _db.Doctors.AnyAsync(d => d.Clinics.Any(c => c.ClinicId == id), cancellationToken))
                {
                    throw new ConflictException(ClinicErrors.InUse);
                }

                _db.Clinics.Remove(clinic);
                await _db.SaveGuardedAsync(cancellationToken);
                return true;
            },
            cancellationToken);

    private async Task<Clinic> FindAsync(long id, CancellationToken cancellationToken) =>
        await _db.Clinics.SingleOrDefaultAsync(c => c.Id == id, cancellationToken)
        ?? throw new NotFoundException(ClinicErrors.NotFound);

    // A friendly early answer. The unique indexes remain the real guard against races.
    private async Task EnsureNamesAreFreeAsync(Clinic clinic, long excludedId, CancellationToken cancellationToken)
    {
        if (await _db.Clinics.AnyAsync(
                c => c.Id != excludedId && c.NameArNormalized == clinic.NameArNormalized,
                cancellationToken))
        {
            throw new ConflictException(ClinicErrors.NameArTaken);
        }

        if (await _db.Clinics.AnyAsync(
                c => c.Id != excludedId && c.NameEnNormalized == clinic.NameEnNormalized,
                cancellationToken))
        {
            throw new ConflictException(ClinicErrors.NameEnTaken);
        }
    }
}
