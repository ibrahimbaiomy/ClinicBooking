using ClinicBooking.Application.DTOs;
using ClinicBooking.Application.Features.Common;
using ClinicBooking.Application.Interfaces;
using ClinicBooking.Domain.Entities;
using ClinicBooking.Domain.Exceptions;
using ClinicBooking.Domain.ValueObjects;

namespace ClinicBooking.Application.Features.Clinics;

/// <summary>Follows <c>SpecialtyService</c> step for step (D50, D55).</summary>
public sealed class ClinicService : IClinicService
{
    private readonly IAppDbContext _db;

    public ClinicService(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<PagedResponse<ClinicResponse>> ListAsync(ListClinicsQuery query, CancellationToken cancellationToken)
    {
        var clinics = _db.Clinics.AsQueryable();

        // Every word of the search must match one of the two names (D49). Address and phone are not searched.
        foreach (var token in SearchText.Normalize(query.Search).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            clinics = clinics.Where(c => c.NameArNormalized.Contains(token) || c.NameEnNormalized.Contains(token));
        }

        var totalCount = await clinics.CountAsync(cancellationToken);

        var items = await Order(clinics, query)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(ClinicMapping.ToResponseExpression)
            .ToListAsync(cancellationToken);

        return new PagedResponse<ClinicResponse>(items, query.Page, query.PageSize, totalCount);
    }

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

        // The client edited a version it read earlier; if the row has moved on, it must reload.
        // The save below is guarded too (EF compares the version it loaded).
        if (!RowVersionCodec.TryDecode(request.RowVersion, out var sent) || !sent.AsSpan().SequenceEqual(clinic.RowVersion))
        {
            throw new ConflictException(ConcurrencyErrors.Conflict);
        }

        clinic.SetNames(request.NameAr!, request.NameEn!);
        clinic.SetContact(request.Address, request.Phone);
        await EnsureNamesAreFreeAsync(clinic, clinic.Id, cancellationToken);

        await SaveGuardedAsync(cancellationToken);

        return clinic.ToResponse();
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken)
    {
        var clinic = await FindAsync(id, cancellationToken);

        _db.Clinics.Remove(clinic);
        await SaveGuardedAsync(cancellationToken);
    }

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
    private static IOrderedQueryable<Clinic> Order(IQueryable<Clinic> clinics, ListClinicsQuery query)
    {
        var descending = string.Equals(query.SortDirection, "desc", StringComparison.OrdinalIgnoreCase);

        IOrderedQueryable<Clinic> ordered;
        if (string.Equals(query.SortBy, ClinicSortFields.NameAr, StringComparison.OrdinalIgnoreCase))
        {
            ordered = descending
                ? clinics.OrderByDescending(c => c.NameArNormalized)
                : clinics.OrderBy(c => c.NameArNormalized);
        }
        else if (string.Equals(query.SortBy, ClinicSortFields.CreatedAt, StringComparison.OrdinalIgnoreCase))
        {
            // Newest first also means the highest id first when rows share a timestamp.
            return descending
                ? clinics.OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id)
                : clinics.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id);
        }
        else
        {
            ordered = descending
                ? clinics.OrderByDescending(c => c.NameEnNormalized)
                : clinics.OrderBy(c => c.NameEnNormalized);
        }

        return ordered.ThenBy(c => c.Id);
    }
}
