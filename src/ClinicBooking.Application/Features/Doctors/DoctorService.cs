using ClinicBooking.Application.DTOs;
using ClinicBooking.Application.Features.Common;
using ClinicBooking.Application.Features.Users;
using ClinicBooking.Application.Interfaces;
using ClinicBooking.Domain.Entities;
using ClinicBooking.Domain.Exceptions;
using ClinicBooking.Domain.Permissions;

namespace ClinicBooking.Application.Features.Doctors;

/// <summary>
/// Doctors (D61). Reading is open to any signed-in user (D57). Writes are authorized here against the
/// doctor's clinics, every assignment counted whether active or not, soft-deleted clinics ignored:
/// create needs doctors.manage in every requested clinic, edit in any of the doctor's clinics
/// (<see cref="IClinicAccess"/>: 404 when none), delete in all of them.
/// </summary>
public sealed class DoctorService : IDoctorService
{
    private const string Manage = Permissions.Doctors.Manage;

    private readonly IAppDbContext _db;
    private readonly IPermissionChecker _permissions;
    private readonly IClinicAccess _access;
    private readonly IDoctorScheduleGuard _guard;
    private readonly IUser _user;
    private readonly TimeProvider _clock;

    public DoctorService(
        IAppDbContext db,
        IPermissionChecker permissions,
        IClinicAccess access,
        IDoctorScheduleGuard guard,
        IUser user,
        TimeProvider clock)
    {
        _db = db;
        _permissions = permissions;
        _access = access;
        _guard = guard;
        _user = user;
        _clock = clock;
    }

    public async Task<PagedResponse<DoctorResponse>> ListAsync(ListDoctorsQuery query, CancellationToken cancellationToken)
    {
        // Split queries are the default (Infrastructure), so the three collections do not multiply rows.
        var doctors = _db.Doctors.MatchingEveryWord(query.Search);

        if (query.ClinicId is { } clinicId)
        {
            doctors = query.IsActive is { } isActive
                ? doctors.Where(d => d.Clinics.Any(c => c.ClinicId == clinicId && c.IsActive == isActive && !c.Clinic.IsDeleted))
                : doctors.Where(d => d.Clinics.Any(c => c.ClinicId == clinicId && !c.Clinic.IsDeleted));
        }

        if (query.SpecialtyId is { } specialtyId)
        {
            doctors = doctors.Where(d => d.Specialties.Any(s => s.SpecialtyId == specialtyId));
        }

        var page = await doctors.ToPageAsync(query, DoctorMapping.ToRowExpression, cancellationToken);
        var today = CairoTime.Today(_clock);

        return new PagedResponse<DoctorResponse>(
            page.Items.Select(row => row.ToResponse(today)).ToList(), page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<DoctorResponse> GetAsync(long id, CancellationToken cancellationToken)
    {
        var row = await _db.Doctors
                      .Where(d => d.Id == id)
                      .Select(DoctorMapping.ToRowExpression)
                      .SingleOrDefaultAsync(cancellationToken)
                  ?? throw new NotFoundException(DoctorErrors.NotFound);

        return row.ToResponse(CairoTime.Today(_clock));
    }

    public async Task<DoctorResponse> CreateAsync(CreateDoctorRequest request, CancellationToken cancellationToken)
    {
        var clinicIds = request.ClinicIds!.Distinct().ToArray();
        var specialtyIds = request.SpecialtyIds!.Distinct().ToArray();

        // Serializable: a clinic or specialty deleted at the same moment either sees this doctor
        // (409 in_use) or makes this create fail, never both succeed.
        var id = await _db.InSerializableTransactionAsync(
            async () =>
            {
                // doctors.manage in every requested clinic; a missing or deleted clinic grants nothing (D57).
                var held = await HeldClinicIdsAsync(cancellationToken);
                if (!clinicIds.All(held.Contains))
                {
                    throw new ForbiddenException(ClinicAccess.ForbiddenKey);
                }

                await EnsureSpecialtiesAvailableAsync(specialtyIds, cancellationToken);

                var doctor = Doctor.Create(request.NameAr!, request.NameEn!);
                foreach (var specialtyId in specialtyIds)
                {
                    doctor.Specialties.Add(new DoctorSpecialty { SpecialtyId = specialtyId });
                }

                foreach (var clinicId in clinicIds)
                {
                    doctor.Clinics.Add(new DoctorClinic { ClinicId = clinicId, IsActive = true });
                }

                DoctorSlotDuration.CreateFor(doctor, request.SlotMinutes!.Value, CairoTime.Today(_clock));

                _db.Doctors.Add(doctor);
                await _db.SaveChangesAsync(cancellationToken);
                return doctor.Id;
            },
            cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    public async Task<DoctorResponse> UpdateAsync(long id, UpdateDoctorRequest request, CancellationToken cancellationToken)
    {
        var doctor = await _db.Doctors.Include(d => d.Specialties).SingleOrDefaultAsync(d => d.Id == id, cancellationToken)
                     ?? throw new NotFoundException(DoctorErrors.NotFound);

        // Authorization before the row version, so a stale version tells nothing to an outsider.
        await _access.RequireAsync(await LiveClinicIdsAsync(id, cancellationToken), Manage, DoctorErrors.NotFound, cancellationToken);
        ConcurrencyGuard.EnsureCurrent(doctor, request.RowVersion);

        var wanted = request.SpecialtyIds!.Distinct().ToArray();

        await _db.InSerializableTransactionAsync(
            async () =>
            {
                await EnsureSpecialtiesAvailableAsync(wanted, cancellationToken);

                // A full replace (D55): rows not wanted any more are deleted (a join table, D35).
                _db.DoctorSpecialties.RemoveRange(doctor.Specialties.Where(s => !wanted.Contains(s.SpecialtyId)).ToList());
                foreach (var specialtyId in wanted.Where(w => doctor.Specialties.All(s => s.SpecialtyId != w)))
                {
                    _db.DoctorSpecialties.Add(new DoctorSpecialty { DoctorId = doctor.Id, SpecialtyId = specialtyId });
                }

                doctor.SetNames(request.NameAr!, request.NameEn!);

                // A change to the specialties alone still moves the doctor's row version (D61).
                _db.MarkModified(doctor);
                await _db.SaveGuardedAsync(cancellationToken);
                return true;
            },
            cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken)
    {
        // Loaded alone: no related row is tracked, so nothing but the doctor itself changes (D35).
        var doctor = await _db.Doctors.SingleOrDefaultAsync(d => d.Id == id, cancellationToken)
                     ?? throw new NotFoundException(DoctorErrors.NotFound);

        var clinicIds = await LiveClinicIdsAsync(id, cancellationToken);
        await _access.RequireAsync(clinicIds, Manage, DoctorErrors.NotFound, cancellationToken);

        var held = await HeldClinicIdsAsync(cancellationToken);
        if (!clinicIds.All(held.Contains))
        {
            throw new ForbiddenException(DoctorErrors.AllClinicsRequired);
        }

        await _guard.EnsureCanDeleteAsync(id, cancellationToken);

        _db.Doctors.Remove(doctor);
        await _db.SaveGuardedAsync(cancellationToken);
    }

    // Every assignment, active or inactive, in a clinic that is not soft-deleted (D57, D61).
    private async Task<long[]> LiveClinicIdsAsync(long doctorId, CancellationToken cancellationToken) =>
        await _db.DoctorClinics
            .Where(c => c.DoctorId == doctorId && !c.Clinic.IsDeleted)
            .Select(c => c.ClinicId)
            .ToArrayAsync(cancellationToken);

    private async Task<HashSet<long>> HeldClinicIdsAsync(CancellationToken cancellationToken)
    {
        if (_user.Id is not { } userId)
        {
            return [];
        }

        return [.. await _permissions.GetClinicIdsWithPermissionAsync(userId, Manage, cancellationToken)];
    }

    // An unknown or soft-deleted specialty cannot be chosen: a 400 on the field (D61).
    private async Task EnsureSpecialtiesAvailableAsync(long[] specialtyIds, CancellationToken cancellationToken)
    {
        var found = await _db.Specialties.CountAsync(s => specialtyIds.Contains(s.Id), cancellationToken);
        if (found != specialtyIds.Length)
        {
            throw new InvalidRequestException(
                UserErrors.ValidationFailed,
                new Dictionary<string, string[]> { ["specialtyIds"] = [DoctorErrors.SpecialtyUnavailable] });
        }
    }
}
