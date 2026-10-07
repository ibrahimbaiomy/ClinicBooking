using ClinicBooking.Application.DTOs;
using ClinicBooking.Application.Features.Clinics;
using ClinicBooking.Application.Features.Common;
using ClinicBooking.Application.Features.Users;
using ClinicBooking.Application.Interfaces;
using ClinicBooking.Domain.Entities;
using ClinicBooking.Domain.Exceptions;
using ClinicBooking.Domain.Permissions;
using ClinicBooking.Domain.ValueObjects;

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

    public async Task<DoctorResponse> ChangeSlotDurationAsync(
        long id,
        ChangeSlotDurationRequest request,
        CancellationToken cancellationToken)
    {
        await EnsureDoctorExistsAsync(id, cancellationToken);
        await _access.RequireAsync(await LiveClinicIdsAsync(id, cancellationToken), Manage, DoctorErrors.NotFound, cancellationToken);

        var effectiveFrom = request.EffectiveFrom!.Value;
        var today = CairoTime.Today(_clock);
        if (effectiveFrom <= today)
        {
            throw new BusinessRuleException(DoctorErrors.EffectiveFromNotFuture);
        }

        // "After the doctor's last active appointment" (D43) needs Appointments: the Phase 2 seam (D61).
        await _guard.EnsureSlotChangeAllowedAsync(id, effectiveFrom, cancellationToken);

        var row = DoctorSlotDuration.Create(id, request.SlotMinutes!.Value, effectiveFrom);

        // At most one pending change: a new one replaces it (history rows are hard-deleted, D35, D61).
        // Existing working hours are not revalidated against the new duration (D61).
        await _db.InSerializableTransactionAsync(
            async () =>
            {
                var pending = await _db.DoctorSlotDurations
                    .Where(s => s.DoctorId == id && s.EffectiveFrom > today)
                    .ToListAsync(cancellationToken);
                if (pending.Count > 0)
                {
                    _db.DoctorSlotDurations.RemoveRange(pending);
                    await _db.SaveChangesAsync(cancellationToken); // before the insert: the date may be the same
                }

                _db.DoctorSlotDurations.Add(row);
                await _db.SaveChangesAsync(cancellationToken);
                return true;
            },
            cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    public async Task<DoctorResponse> AddClinicAsync(long id, long clinicId, CancellationToken cancellationToken)
    {
        // Serializable: a clinic deleted at the same moment either sees this assignment (409 in_use) or wins.
        await _db.InSerializableTransactionAsync(
            async () =>
            {
                await EnsureDoctorExistsAsync(id, cancellationToken);

                // Active or not: a returning doctor is reactivated, never assigned twice (D61). The unique
                // index is the real guard and answers with the same key.
                if (await _db.DoctorClinics.AnyAsync(c => c.DoctorId == id && c.ClinicId == clinicId, cancellationToken))
                {
                    throw new ConflictException(DoctorErrors.ClinicAlreadyAssigned);
                }

                _db.DoctorClinics.Add(new DoctorClinic { DoctorId = id, ClinicId = clinicId, IsActive = true });
                await _db.SaveChangesAsync(cancellationToken);
                return true;
            },
            cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    public async Task<DoctorResponse> ActivateClinicAsync(long id, long clinicId, CancellationToken cancellationToken)
    {
        // Serializable, like saving hours: the cross-clinic check and the switch happen together (D32).
        await _db.InSerializableTransactionAsync(
            async () =>
            {
                await EnsureDoctorExistsAsync(id, cancellationToken);
                var assignment = await FindAssignmentAsync(id, clinicId, cancellationToken);
                if (assignment.IsActive)
                {
                    return true;
                }

                // The stored periods were ignored while inactive; they must fit again now (D61).
                WeeklyPeriod.EnsureNoOverlapWithOtherClinics(
                    assignment.WorkingHours.Select(p => p.ToWeeklyPeriod()).ToList(),
                    await OtherActivePeriodsAsync(id, assignment.Id, cancellationToken),
                    withPeriodIndex: false); // no list was sent: only the other clinic is named

                assignment.IsActive = true;
                await _db.SaveGuardedAsync(cancellationToken);
                return true;
            },
            cancellationToken);

        return await GetAsync(id, cancellationToken);
    }

    public async Task<DoctorResponse> DeactivateClinicAsync(long id, long clinicId, CancellationToken cancellationToken)
    {
        await EnsureDoctorExistsAsync(id, cancellationToken);
        var assignment = await FindAssignmentAsync(id, clinicId, cancellationToken);

        if (assignment.IsActive)
        {
            // Future appointments in that clinic are a Phase 2 concern (D61).
            await _guard.EnsureCanDeactivateAsync(id, clinicId, cancellationToken);

            assignment.IsActive = false;
            await _db.SaveGuardedAsync(cancellationToken);
        }

        return await GetAsync(id, cancellationToken);
    }

    public async Task<WorkingHoursResponse> GetWorkingHoursAsync(long id, long clinicId, CancellationToken cancellationToken)
    {
        await EnsureDoctorExistsAsync(id, cancellationToken);

        // The read is open to everyone, so the clinic is checked here (the policy does it on the writes).
        if (!await _db.Clinics.AnyAsync(c => c.Id == clinicId, cancellationToken))
        {
            throw new NotFoundException(ClinicErrors.NotFound);
        }

        var assignment = await _db.DoctorClinics
                             .Include(c => c.WorkingHours)
                             .AsNoTracking()
                             .SingleOrDefaultAsync(c => c.DoctorId == id && c.ClinicId == clinicId, cancellationToken)
                         ?? throw new NotFoundException(DoctorErrors.ClinicNotAssigned);

        return ToWorkingHoursResponse(assignment);
    }

    public async Task<WorkingHoursResponse> ReplaceWorkingHoursAsync(
        long id,
        long clinicId,
        ReplaceWorkingHoursRequest request,
        CancellationToken cancellationToken)
    {
        var periods = request.Periods!
            .Select(p => WeeklyPeriod.Create(p.DayOfWeek!.Value, p.Start!.Value, p.End!.Value))
            .ToList();

        // Serializable: two clinics saving overlapping hours for the same doctor at once cannot both pass (D32).
        var assignment = await _db.InSerializableTransactionAsync(
            async () =>
            {
                await EnsureDoctorExistsAsync(id, cancellationToken);
                var assignment = await FindAssignmentAsync(id, clinicId, cancellationToken);
                ConcurrencyGuard.EnsureCurrent(assignment, request.RowVersion);

                WeeklyPeriod.EnsureNoOverlap(periods);

                // Today's duration (Cairo); a scheduled change does not revalidate stored periods (D61).
                WeeklyPeriod.EnsureEachHoldsASlot(periods, await SlotMinutesTodayAsync(id, cancellationToken));

                // An inactive assignment's periods are ignored by the cross-clinic check until it is
                // reactivated, which runs the check then (D61).
                if (assignment.IsActive)
                {
                    WeeklyPeriod.EnsureNoOverlapWithOtherClinics(
                        periods, await OtherActivePeriodsAsync(id, assignment.Id, cancellationToken));
                }

                await _guard.EnsureWorkingHoursChangeAllowedAsync(id, clinicId, periods, cancellationToken);

                _db.WorkingHourPeriods.RemoveRange(assignment.WorkingHours.ToList());
                foreach (var period in periods)
                {
                    assignment.WorkingHours.Add(WorkingHourPeriod.Create(period));
                }

                // The week is part of the assignment: its row version moves with every save (D61).
                _db.MarkModified(assignment);
                await _db.SaveGuardedAsync(cancellationToken);
                return assignment;
            },
            cancellationToken);

        return ToWorkingHoursResponse(assignment);
    }

    private async Task EnsureDoctorExistsAsync(long id, CancellationToken cancellationToken)
    {
        if (!await _db.Doctors.AnyAsync(d => d.Id == id, cancellationToken))
        {
            throw new NotFoundException(DoctorErrors.NotFound);
        }
    }

    private async Task<DoctorClinic> FindAssignmentAsync(long id, long clinicId, CancellationToken cancellationToken) =>
        await _db.DoctorClinics
            .Include(c => c.WorkingHours)
            .SingleOrDefaultAsync(c => c.DoctorId == id && c.ClinicId == clinicId, cancellationToken)
        ?? throw new NotFoundException(DoctorErrors.ClinicNotAssigned);

    private async Task<int> SlotMinutesTodayAsync(long id, CancellationToken cancellationToken)
    {
        var today = CairoTime.Today(_clock);
        return await _db.DoctorSlotDurations
            .Where(s => s.DoctorId == id && s.EffectiveFrom <= today)
            .OrderByDescending(s => s.EffectiveFrom)
            .Select(s => s.SlotMinutes)
            .FirstAsync(cancellationToken);
    }

    // The doctor's periods in every other active assignment of a live clinic (D32, D61).
    private async Task<List<(WeeklyPeriod Period, long ClinicId)>> OtherActivePeriodsAsync(
        long id,
        long assignmentId,
        CancellationToken cancellationToken)
    {
        var rows = await _db.DoctorClinics
            .Where(c => c.DoctorId == id && c.Id != assignmentId && c.IsActive && !c.Clinic.IsDeleted)
            .SelectMany(c => c.WorkingHours, (c, p) => new { c.ClinicId, Period = p })
            .ToListAsync(cancellationToken);

        return rows.Select(r => (r.Period.ToWeeklyPeriod(), r.ClinicId)).ToList();
    }

    private static WorkingHoursResponse ToWorkingHoursResponse(DoctorClinic assignment) =>
        new(
            assignment.DoctorId,
            assignment.ClinicId,
            assignment.IsActive,
            assignment.WorkingHours
                .OrderBy(p => p.DayOfWeek)
                .ThenBy(p => p.Start)
                .Select(p => new WorkingHourPeriodResponse((int)p.DayOfWeek, p.Start, p.End))
                .ToList(),
            RowVersionCodec.Encode(assignment.RowVersion));

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
