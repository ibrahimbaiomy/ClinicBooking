using ClinicBooking.Application.DTOs;
using ClinicBooking.Application.Features.Common;
using ClinicBooking.Application.Interfaces;
using ClinicBooking.Domain.Entities;
using ClinicBooking.Domain.Exceptions;

namespace ClinicBooking.Application.Features.Specialties;

public sealed class SpecialtyService : ISpecialtyService
{
    private readonly IAppDbContext _db;

    public SpecialtyService(IAppDbContext db)
    {
        _db = db;
    }

    public Task<PagedResponse<SpecialtyResponse>> ListAsync(
        ListSpecialtiesQuery query,
        CancellationToken cancellationToken) =>
        _db.Specialties
            .MatchingEveryWord(query.Search)
            .ToPageAsync(query, SpecialtyMapping.ToResponseExpression, cancellationToken);

    public async Task<SpecialtyResponse> GetAsync(long id, CancellationToken cancellationToken)
    {
        return await _db.Specialties
                   .Where(s => s.Id == id)
                   .Select(SpecialtyMapping.ToResponseExpression)
                   .SingleOrDefaultAsync(cancellationToken)
               ?? throw new NotFoundException(SpecialtyErrors.NotFound);
    }

    public async Task<SpecialtyResponse> CreateAsync(CreateSpecialtyRequest request, CancellationToken cancellationToken)
    {
        var specialty = Specialty.Create(request.NameAr!, request.NameEn!);
        await EnsureNamesAreFreeAsync(specialty, excludedId: 0, cancellationToken);

        _db.Specialties.Add(specialty);
        await _db.SaveChangesAsync(cancellationToken);

        return specialty.ToResponse();
    }

    public async Task<SpecialtyResponse> UpdateAsync(
        long id,
        UpdateSpecialtyRequest request,
        CancellationToken cancellationToken)
    {
        var specialty = await FindAsync(id, cancellationToken);
        ConcurrencyGuard.EnsureCurrent(specialty, request.RowVersion);

        specialty.SetNames(request.NameAr!, request.NameEn!);
        await EnsureNamesAreFreeAsync(specialty, specialty.Id, cancellationToken);

        await _db.SaveGuardedAsync(cancellationToken);

        return specialty.ToResponse();
    }

    public Task DeleteAsync(long id, CancellationToken cancellationToken) =>
        // Serializable: a doctor given this specialty at the same moment either is seen here or fails (D61).
        _db.InSerializableTransactionAsync(
            async () =>
            {
                var specialty = await FindAsync(id, cancellationToken);

                // No cascade; soft-deleted doctors keep their reference and do not count (D50).
                if (await _db.Doctors.AnyAsync(d => d.Specialties.Any(s => s.SpecialtyId == id), cancellationToken))
                {
                    throw new ConflictException(SpecialtyErrors.InUse);
                }

                _db.Specialties.Remove(specialty);
                await _db.SaveGuardedAsync(cancellationToken);
                return true;
            },
            cancellationToken);

    private async Task<Specialty> FindAsync(long id, CancellationToken cancellationToken) =>
        await _db.Specialties.SingleOrDefaultAsync(s => s.Id == id, cancellationToken)
        ?? throw new NotFoundException(SpecialtyErrors.NotFound);

    // A friendly early answer. The unique indexes remain the real guard against races (D43 pattern).
    private async Task EnsureNamesAreFreeAsync(Specialty specialty, long excludedId, CancellationToken cancellationToken)
    {
        if (await _db.Specialties.AnyAsync(
                s => s.Id != excludedId && s.NameArNormalized == specialty.NameArNormalized,
                cancellationToken))
        {
            throw new ConflictException(SpecialtyErrors.NameArTaken);
        }

        if (await _db.Specialties.AnyAsync(
                s => s.Id != excludedId && s.NameEnNormalized == specialty.NameEnNormalized,
                cancellationToken))
        {
            throw new ConflictException(SpecialtyErrors.NameEnTaken);
        }
    }
}
