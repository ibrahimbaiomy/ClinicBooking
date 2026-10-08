using ClinicBooking.Application.DTOs;
using ClinicBooking.Application.Features.Common;
using ClinicBooking.Application.Interfaces;
using ClinicBooking.Domain.Entities;
using ClinicBooking.Domain.Exceptions;
using ClinicBooking.Domain.ValueObjects;

namespace ClinicBooking.Application.Features.Patients;

/// <summary>
/// Patients (D38, D44, D63), following <c>ClinicService</c>. Names and phones are personal data: this class
/// never logs them and never puts them in an exception message.
/// </summary>
public sealed class PatientService : IPatientService
{
    private readonly IAppDbContext _db;
    private readonly IPatientScheduleGuard _guard;

    public PatientService(IAppDbContext db, IPatientScheduleGuard guard)
    {
        _db = db;
        _guard = guard;
    }

    public async Task<PagedResponse<PatientResponse>> ListAsync(ListPatientsQuery query, CancellationToken cancellationToken)
    {
        var patients = Search(_db.Patients, query.Search);

        var totalCount = await patients.CountAsync(cancellationToken);
        var items = await Order(patients, query)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(PatientMapping.ToResponseExpression)
            .ToListAsync(cancellationToken);

        return new PagedResponse<PatientResponse>(items, query.Page, query.PageSize, totalCount);
    }

    public async Task<PatientResponse> GetAsync(long id, CancellationToken cancellationToken) =>
        await _db.Patients
            .Where(p => p.Id == id)
            .Select(PatientMapping.ToResponseExpression)
            .SingleOrDefaultAsync(cancellationToken)
        ?? throw new NotFoundException(PatientErrors.NotFound);

    public async Task<PatientResponse> CreateAsync(CreatePatientRequest request, CancellationToken cancellationToken)
    {
        var patient = Patient.Create(request.Name!, request.Phone!);

        _db.Patients.Add(patient);
        await _db.SaveChangesAsync(cancellationToken);

        return patient.ToResponse();
    }

    public async Task<PatientResponse> UpdateAsync(long id, UpdatePatientRequest request, CancellationToken cancellationToken)
    {
        var patient = await FindAsync(id, cancellationToken);
        ConcurrencyGuard.EnsureCurrent(patient, request.RowVersion);

        patient.Set(request.Name!, request.Phone!);
        await _db.SaveGuardedAsync(cancellationToken);

        return patient.ToResponse();
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken)
    {
        var patient = await FindAsync(id, cancellationToken);

        // Future appointments are a Phase 2 concern (D63).
        await _guard.EnsureCanDeleteAsync(id, cancellationToken);

        _db.Patients.Remove(patient);
        await _db.SaveGuardedAsync(cancellationToken);
    }

    private async Task<Patient> FindAsync(long id, CancellationToken cancellationToken) =>
        await _db.Patients.SingleOrDefaultAsync(p => p.Id == id, cancellationToken)
        ?? throw new NotFoundException(PatientErrors.NotFound);

    /// <summary>
    /// One box, two meanings (D63): a phone prefix or contains match on the stored E.164 value, or every word
    /// of the name. Name search is a scan (no full-text), accepted for Phase 1.
    /// </summary>
    private static IQueryable<Patient> Search(IQueryable<Patient> patients, string? search)
    {
        var term = PatientSearchTerm.Parse(search);
        switch (term.Kind)
        {
            case PatientSearchKind.PhonePrefix:
                return patients.Where(p => p.Phone.StartsWith(term.Phone));
            case PatientSearchKind.PhoneContains:
                return patients.Where(p => p.Phone.Contains(term.Phone));
        }

        foreach (var token in SearchText.Normalize(search).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            patients = patients.Where(p => p.NameNormalized.Contains(token));
        }

        return patients;
    }

    // The normalised name gives Arabic alphabetical order, ignoring hamza forms and diacritics; Id breaks ties.
    private static IOrderedQueryable<Patient> Order(IQueryable<Patient> patients, ListPatientsQuery query)
    {
        var descending = string.Equals(query.SortDirection, "desc", StringComparison.OrdinalIgnoreCase);

        if (string.Equals(query.SortBy, PatientSortFields.CreatedAt, StringComparison.OrdinalIgnoreCase))
        {
            return descending
                ? patients.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id)
                : patients.OrderBy(p => p.CreatedAt).ThenBy(p => p.Id);
        }

        return descending
            ? patients.OrderByDescending(p => p.NameNormalized).ThenBy(p => p.Id)
            : patients.OrderBy(p => p.NameNormalized).ThenBy(p => p.Id);
    }
}
