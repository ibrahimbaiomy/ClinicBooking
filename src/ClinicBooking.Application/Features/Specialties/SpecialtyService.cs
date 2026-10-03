using ClinicBooking.Application.DTOs;
using ClinicBooking.Application.Features.Common;
using ClinicBooking.Application.Interfaces;
using ClinicBooking.Domain.Entities;
using ClinicBooking.Domain.Exceptions;
using ClinicBooking.Domain.ValueObjects;

namespace ClinicBooking.Application.Features.Specialties;

public sealed class SpecialtyService : ISpecialtyService
{
    private readonly IAppDbContext _db;

    public SpecialtyService(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResponse<SpecialtyResponse>> ListAsync(
        ListSpecialtiesQuery query,
        CancellationToken cancellationToken)
    {
        var specialties = _db.Specialties.AsQueryable();

        // Every word of the search must match one of the two names (D49).
        foreach (var token in SearchText.Normalize(query.Search).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            specialties = specialties.Where(s => s.NameArNormalized.Contains(token) || s.NameEnNormalized.Contains(token));
        }

        var totalCount = await specialties.CountAsync(cancellationToken);

        var items = await Order(specialties, query)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(SpecialtyMapping.ToResponseExpression)
            .ToListAsync(cancellationToken);

        return new PagedResponse<SpecialtyResponse>(items, query.Page, query.PageSize, totalCount);
    }

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

        // The client edited a version it read earlier; if the row has moved on, it must reload.
        // The save below is guarded too (EF compares the version it loaded), so a change that
        // slips in between this check and the save is caught as well.
        if (!RowVersionCodec.TryDecode(request.RowVersion, out var sent) || !sent.AsSpan().SequenceEqual(specialty.RowVersion))
        {
            throw new ConflictException(ConcurrencyErrors.Conflict);
        }

        specialty.SetNames(request.NameAr!, request.NameEn!);
        await EnsureNamesAreFreeAsync(specialty, specialty.Id, cancellationToken);

        await SaveGuardedAsync(cancellationToken);

        return specialty.ToResponse();
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken)
    {
        var specialty = await FindAsync(id, cancellationToken);

        _db.Specialties.Remove(specialty);
        await SaveGuardedAsync(cancellationToken);
    }

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

    private async Task SaveGuardedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(ConcurrencyErrors.Conflict);
        }
    }

    // Sorting by the normalised text gives Arabic alphabetical order, ignoring hamza forms and
    // diacritics. Id breaks ties so pages are stable.
    private static IOrderedQueryable<Specialty> Order(IQueryable<Specialty> specialties, ListSpecialtiesQuery query)
    {
        var descending = string.Equals(query.SortDirection, "desc", StringComparison.OrdinalIgnoreCase);

        IOrderedQueryable<Specialty> ordered;
        if (string.Equals(query.SortBy, SpecialtySortFields.NameAr, StringComparison.OrdinalIgnoreCase))
        {
            ordered = descending
                ? specialties.OrderByDescending(s => s.NameArNormalized)
                : specialties.OrderBy(s => s.NameArNormalized);
        }
        else if (string.Equals(query.SortBy, SpecialtySortFields.CreatedAt, StringComparison.OrdinalIgnoreCase))
        {
            // Newest first also means the highest id first when rows share a timestamp.
            return descending
                ? specialties.OrderByDescending(s => s.CreatedAt).ThenByDescending(s => s.Id)
                : specialties.OrderBy(s => s.CreatedAt).ThenBy(s => s.Id);
        }
        else
        {
            ordered = descending
                ? specialties.OrderByDescending(s => s.NameEnNormalized)
                : specialties.OrderBy(s => s.NameEnNormalized);
        }

        return ordered.ThenBy(s => s.Id);
    }
}
