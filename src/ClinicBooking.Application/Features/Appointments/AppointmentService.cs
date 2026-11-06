using ClinicBooking.Application.DTOs;
using ClinicBooking.Application.Features.Common;
using ClinicBooking.Application.Features.Doctors;
using ClinicBooking.Application.Features.Users;
using ClinicBooking.Application.Interfaces;
using ClinicBooking.Domain.Entities;
using ClinicBooking.Domain.Enums;
using ClinicBooking.Domain.Exceptions;
using ClinicBooking.Domain.Permissions;
using ClinicBooking.Domain.ValueObjects;
using Microsoft.Extensions.Options;

namespace ClinicBooking.Application.Features.Appointments;

/// <summary>
/// Appointments (D31, D41, D43, D66). Endpoints with the clinic in the route are authorized by the policy;
/// an appointment addressed by its reference is authorized here against its clinic (<see cref="IClinicAccess"/>:
/// 404 when the caller holds nothing there). Rules that read other rows run in one serializable transaction,
/// and the unique index on (doctor, start) is the real guard against double booking. Patient names and phones
/// never reach a log or an exception message.
/// </summary>
public sealed class AppointmentService : IAppointmentService
{
    /// <summary>How many overlapping appointments the patient-overlap warning lists (as D63).</summary>
    public const int MaxMatches = 5;

    private const int MaxReferenceAttempts = 10;

    private readonly IAppDbContext _db;
    private readonly IPermissionChecker _permissions;
    private readonly IClinicAccess _access;
    private readonly IUser _user;
    private readonly TimeProvider _clock;
    private readonly AppointmentOptions _options;

    public AppointmentService(
        IAppDbContext db,
        IPermissionChecker permissions,
        IClinicAccess access,
        IUser user,
        TimeProvider clock,
        IOptions<AppointmentOptions> options)
    {
        _db = db;
        _permissions = permissions;
        _access = access;
        _user = user;
        _clock = clock;
        _options = options.Value;
    }

    public async Task<AppointmentResponse> BookAsync(long clinicId, BookAppointmentRequest request, CancellationToken cancellationToken)
    {
        var doctorId = request.DoctorId!.Value;
        var patientId = request.PatientId!.Value;
        var start = request.StartUtc!.Value.ToUniversalTime();

        var reference = await _db.InSerializableTransactionAsync(
            async () =>
            {
                if (!await _db.Patients.AnyAsync(p => p.Id == patientId, cancellationToken))
                {
                    throw new InvalidRequestException(
                        UserErrors.ValidationFailed,
                        new Dictionary<string, string[]> { ["patientId"] = [AppointmentErrors.PatientUnavailable] });
                }

                var end = await CheckTimeAsync(doctorId, clinicId, start, excludedId: 0, cancellationToken);
                await EnsurePatientFreeAsync(patientId, start, end, excludedId: 0, request.ConfirmPatientOverlap == true, cancellationToken);

                var appointment = Appointment.Book(await NewReferenceAsync(cancellationToken), doctorId, clinicId, patientId, start, end);
                _db.Appointments.Add(appointment);
                await _db.SaveChangesAsync(cancellationToken);
                return appointment.BookingReference;
            },
            cancellationToken);

        return await ResponseAsync(reference, cancellationToken);
    }

    public async Task<IReadOnlyList<AppointmentResponse>> ListDayAsync(
        long clinicId,
        DayAppointmentsQuery query,
        CancellationToken cancellationToken)
    {
        var date = query.Date!.Value;
        var from = CairoTime.StartOfDay(date);
        var to = CairoTime.StartOfDay(date.AddDays(1));

        var appointments = _db.Appointments.Where(a => a.ClinicId == clinicId && a.StartUtc >= from && a.StartUtc < to);
        if (query.DoctorId is { } doctorId)
        {
            appointments = appointments.Where(a => a.DoctorId == doctorId);
        }

        // Every status: the desk sees cancellations too (D66).
        return await ProjectAsync(appointments, cancellationToken);
    }

    public async Task<AvailableSlotsResponse> AvailableSlotsAsync(
        long clinicId,
        long doctorId,
        AvailableSlotsQuery query,
        CancellationToken cancellationToken)
    {
        // Booking or rescheduling in that clinic (D66); the clinic is in the route, so a refusal is a 403.
        if (_user.Id is not { } userId
            || !(await _permissions.HasClinicPermissionAsync(userId, clinicId, Permissions.Appointments.Book, cancellationToken)
                 || await _permissions.HasClinicPermissionAsync(userId, clinicId, Permissions.Appointments.Reschedule, cancellationToken)))
        {
            throw new ForbiddenException(ClinicAccess.ForbiddenKey);
        }

        if (!await _db.Doctors.AnyAsync(d => d.Id == doctorId, cancellationToken))
        {
            throw new NotFoundException(DoctorErrors.NotFound);
        }

        var assignment = await _db.DoctorClinics
                             .Include(c => c.WorkingHours)
                             .AsNoTracking()
                             .SingleOrDefaultAsync(c => c.DoctorId == doctorId && c.ClinicId == clinicId, cancellationToken)
                         ?? throw new NotFoundException(DoctorErrors.ClinicNotAssigned);

        var date = query.Date!.Value;
        var slotMinutes = await _db.SlotMinutesOnAsync(doctorId, date, cancellationToken) ?? 0;
        var empty = new AvailableSlotsResponse(doctorId, clinicId, date, slotMinutes, []);

        var now = _clock.GetUtcNow();
        var today = CairoTime.DateOf(now);
        if (!assignment.IsActive
            || slotMinutes == 0
            || date < today
            || date > today.AddDays(_options.MaxDaysAhead)
            || await _db.DoctorLeaveDays.AnyAsync(l => l.DoctorId == doctorId && l.Date == date, cancellationToken))
        {
            return empty;
        }

        var grid = SlotGrid.For(date, assignment.WorkingHours.Select(p => p.ToWeeklyPeriod()), slotMinutes)
            .Where(s => s.StartUtc > now)
            .ToList();
        if (grid.Count == 0)
        {
            return empty;
        }

        // The doctor's appointments in any clinic (D32): a slot that overlaps one is taken.
        var first = grid[0].StartUtc;
        var last = grid[^1].EndUtc;
        var taken = await _db.Appointments
            .Where(a => a.DoctorId == doctorId && a.Status != AppointmentStatus.Cancelled && a.StartUtc < last && a.EndUtc > first)
            .Select(a => new { a.StartUtc, a.EndUtc })
            .ToListAsync(cancellationToken);

        var free = grid
            .Where(s => !taken.Any(t => t.StartUtc < s.EndUtc && s.StartUtc < t.EndUtc))
            .Select(s => new SlotResponse(s.StartUtc, s.EndUtc))
            .ToList();

        return empty with { Slots = free };
    }

    public async Task<AppointmentResponse> GetAsync(string reference, CancellationToken cancellationToken)
    {
        var normalized = BookingReference.Normalize(reference) ?? throw new NotFoundException(AppointmentErrors.NotFound);
        var clinicId = await _db.Appointments
                           .Where(a => a.BookingReference == normalized)
                           .Select(a => (long?)a.ClinicId)
                           .SingleOrDefaultAsync(cancellationToken)
                       ?? throw new NotFoundException(AppointmentErrors.NotFound);

        await _access.RequireAsync([clinicId], Permissions.Appointments.Read, AppointmentErrors.NotFound, cancellationToken);

        return await ResponseAsync(normalized, cancellationToken);
    }

    public async Task<AppointmentResponse> RescheduleAsync(
        string reference,
        RescheduleAppointmentRequest request,
        CancellationToken cancellationToken)
    {
        var appointment = await FindAuthorizedAsync(reference, Permissions.Appointments.Reschedule, request.RowVersion, cancellationToken);
        var now = _clock.GetUtcNow();
        appointment.EnsureBookedAndNotStarted(now);

        var start = request.StartUtc!.Value.ToUniversalTime();
        if (start == appointment.StartUtc)
        {
            return await ResponseAsync(appointment.BookingReference, cancellationToken); // nothing to move (D66)
        }

        await _db.InSerializableTransactionAsync(
            async () =>
            {
                // Deleting a patient cancels their future appointments (D66); this guards every other path.
                if (!await _db.Patients.AnyAsync(p => p.Id == appointment.PatientId, cancellationToken))
                {
                    throw new BusinessRuleException(AppointmentErrors.PatientUnavailable);
                }

                var end = await CheckTimeAsync(appointment.DoctorId, appointment.ClinicId, start, appointment.Id, cancellationToken);
                await EnsurePatientFreeAsync(
                    appointment.PatientId, start, end, appointment.Id, request.ConfirmPatientOverlap == true, cancellationToken);

                appointment.Reschedule(start, end, now);
                await _db.SaveGuardedAsync(cancellationToken);
                return true;
            },
            cancellationToken);

        return await ResponseAsync(appointment.BookingReference, cancellationToken);
    }

    public async Task<AppointmentResponse> CancelAsync(
        string reference,
        ChangeAppointmentStatusRequest request,
        CancellationToken cancellationToken)
    {
        var appointment = await FindAuthorizedAsync(reference, Permissions.Appointments.Cancel, request.RowVersion, cancellationToken);
        appointment.Cancel(_clock.GetUtcNow());
        await _db.SaveGuardedAsync(cancellationToken);
        return await ResponseAsync(appointment.BookingReference, cancellationToken);
    }

    public Task<AppointmentResponse> CompleteAsync(
        string reference,
        ChangeAppointmentStatusRequest request,
        CancellationToken cancellationToken) =>
        RecordAsync(reference, AppointmentStatus.Completed, request, cancellationToken);

    public Task<AppointmentResponse> MarkNoShowAsync(
        string reference,
        ChangeAppointmentStatusRequest request,
        CancellationToken cancellationToken) =>
        RecordAsync(reference, AppointmentStatus.NoShow, request, cancellationToken);

    private async Task<AppointmentResponse> RecordAsync(
        string reference,
        AppointmentStatus outcome,
        ChangeAppointmentStatusRequest request,
        CancellationToken cancellationToken)
    {
        var appointment = await FindAuthorizedAsync(reference, Permissions.Appointments.Record, request.RowVersion, cancellationToken);
        appointment.Record(outcome, _clock.GetUtcNow());
        await _db.SaveGuardedAsync(cancellationToken);
        return await ResponseAsync(appointment.BookingReference, cancellationToken);
    }

    /// <summary>
    /// Loads the appointment for a change: 404 when unknown or when the caller holds nothing in its clinic, 403
    /// when they hold something else there (D6, D57); the row version after authorization, so a stale version
    /// tells nothing to an outsider.
    /// </summary>
    private async Task<Appointment> FindAuthorizedAsync(
        string reference,
        string permission,
        string? rowVersion,
        CancellationToken cancellationToken)
    {
        var normalized = BookingReference.Normalize(reference) ?? throw new NotFoundException(AppointmentErrors.NotFound);
        var appointment = await _db.Appointments.SingleOrDefaultAsync(a => a.BookingReference == normalized, cancellationToken)
                          ?? throw new NotFoundException(AppointmentErrors.NotFound);

        await _access.RequireAsync([appointment.ClinicId], permission, AppointmentErrors.NotFound, cancellationToken);
        ConcurrencyGuard.EnsureCurrent(appointment, rowVersion);
        return appointment;
    }

    /// <summary>
    /// The booking rules for one start (D41, D43, D66), in this order: the doctor active in the clinic, a future
    /// start, at most the booking limit ahead, not a leave day, on the grid; then no other appointment of the
    /// doctor overlapping it in any clinic (409 slot_taken; the unique index is the real guard). Returns the end.
    /// </summary>
    private async Task<DateTimeOffset> CheckTimeAsync(
        long doctorId,
        long clinicId,
        DateTimeOffset start,
        long excludedId,
        CancellationToken cancellationToken)
    {
        var assignment = await _db.DoctorClinics
                             .Include(c => c.WorkingHours)
                             .SingleOrDefaultAsync(
                                 c => c.DoctorId == doctorId && c.ClinicId == clinicId && c.IsActive
                                      && !c.Clinic.IsDeleted && _db.Doctors.Any(d => d.Id == c.DoctorId),
                                 cancellationToken)
                         ?? throw new BusinessRuleException(AppointmentErrors.DoctorNotAvailable);

        var now = _clock.GetUtcNow();
        if (start <= now)
        {
            throw new BusinessRuleException(AppointmentErrors.InPast);
        }

        var date = CairoTime.DateOf(start);
        if (date > CairoTime.DateOf(now).AddDays(_options.MaxDaysAhead))
        {
            throw new BusinessRuleException(AppointmentErrors.TooFarAhead);
        }

        if (await _db.DoctorLeaveDays.AnyAsync(l => l.DoctorId == doctorId && l.Date == date, cancellationToken))
        {
            throw new BusinessRuleException(AppointmentErrors.OnLeave);
        }

        var slotMinutes = await _db.SlotMinutesOnAsync(doctorId, date, cancellationToken)
                          ?? throw new BusinessRuleException(AppointmentErrors.OffGrid);
        var slot = SlotGrid.For(date, assignment.WorkingHours.Select(p => p.ToWeeklyPeriod()), slotMinutes)
                       .FirstOrDefault(s => s.StartUtc == start)
                   ?? throw new BusinessRuleException(AppointmentErrors.OffGrid);

        if (await _db.Appointments.AnyAsync(
                a => a.DoctorId == doctorId && a.Id != excludedId && a.Status != AppointmentStatus.Cancelled
                     && a.StartUtc < slot.EndUtc && slot.StartUtc < a.EndUtc,
                cancellationToken))
        {
            throw new ConflictException(AppointmentErrors.SlotTaken);
        }

        return slot.EndUtc;
    }

    /// <summary>
    /// The patient-overlap warning (D41, D63's mechanism, D66): another booked appointment of the patient
    /// overlapping this time makes a 409 and nothing is saved, unless the user confirmed. The details of a match
    /// go only to a caller holding appointments.read in its clinic; everyone learns the count. Not a constraint:
    /// two bookings at the same moment may both pass (accepted, D66).
    /// </summary>
    private async Task EnsurePatientFreeAsync(
        long patientId,
        DateTimeOffset start,
        DateTimeOffset end,
        long excludedId,
        bool confirmed,
        CancellationToken cancellationToken)
    {
        if (confirmed)
        {
            return;
        }

        var overlapping = _db.Appointments.Where(
            a => a.PatientId == patientId && a.Id != excludedId && a.Status == AppointmentStatus.Booked
                 && a.StartUtc < end && start < a.EndUtc);
        var count = await overlapping.CountAsync(cancellationToken);
        if (count == 0)
        {
            return;
        }

        var readable = _user.Id is { } userId
            ? await _permissions.GetClinicIdsWithPermissionAsync(userId, Permissions.Appointments.Read, cancellationToken)
            : [];

        var matches = readable.Count == 0
            ? []
            : await (from a in overlapping.Where(a => readable.Contains(a.ClinicId))
                     join d in _db.Doctors.IgnoreQueryFilters() on a.DoctorId equals d.Id
                     join c in _db.Clinics on a.ClinicId equals c.Id
                     orderby a.StartUtc, a.Id
                     select new OverlapMatch(a.StartUtc, a.EndUtc, d.Id, d.NameAr, d.NameEn, c.Id, c.NameAr, c.NameEn))
                .Take(MaxMatches)
                .ToListAsync(cancellationToken);

        throw new PatientOverlapException(count, matches);
    }

    /// <summary>A fresh reference; checked before the insert and regenerated on a collision (D41).</summary>
    private async Task<string> NewReferenceAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MaxReferenceAttempts; attempt++)
        {
            var reference = BookingReference.New();
            if (!await _db.Appointments.AnyAsync(a => a.BookingReference == reference, cancellationToken))
            {
                return reference;
            }
        }

        throw new InvalidOperationException("No free booking reference was found.");
    }

    private async Task<AppointmentResponse> ResponseAsync(string reference, CancellationToken cancellationToken) =>
        (await ProjectAsync(_db.Appointments.Where(a => a.BookingReference == reference), cancellationToken)).Single();

    /// <summary>
    /// Appointments with their doctor, clinic and patient. A deleted doctor or patient still shows, flagged
    /// (D66): the query filters are bypassed for the names. The patient's name and phone go only to a caller
    /// holding patients.read.
    /// </summary>
    private async Task<IReadOnlyList<AppointmentResponse>> ProjectAsync(
        IQueryable<Appointment> appointments,
        CancellationToken cancellationToken)
    {
        var mayReadPatients = _user.Id is { } userId
                              && await _permissions.HasGlobalPermissionAsync(userId, Permissions.Patients.Read, cancellationToken);

        var rows = await (from a in appointments
                          join d in _db.Doctors.IgnoreQueryFilters() on a.DoctorId equals d.Id
                          join c in _db.Clinics.IgnoreQueryFilters() on a.ClinicId equals c.Id
                          join p in _db.Patients.IgnoreQueryFilters() on a.PatientId equals p.Id
                          orderby a.StartUtc, a.Id
                          select new
                          {
                              a.BookingReference,
                              a.Status,
                              a.StartUtc,
                              a.EndUtc,
                              Doctor = new AppointmentDoctorResponse(d.Id, d.NameAr, d.NameEn, d.IsDeleted),
                              Clinic = new AppointmentClinicResponse(c.Id, c.NameAr, c.NameEn),
                              PatientId = p.Id,
                              PatientName = mayReadPatients ? p.Name : null,
                              PatientPhone = mayReadPatients ? p.Phone : null,
                              PatientDeleted = p.IsDeleted,
                              a.CreatedAt,
                              a.UpdatedAt,
                              a.RowVersion,
                          })
            .ToListAsync(cancellationToken);

        return rows
            .Select(r => new AppointmentResponse(
                r.BookingReference,
                StatusText(r.Status),
                r.StartUtc,
                r.EndUtc,
                r.Doctor,
                r.Clinic,
                new AppointmentPatientResponse(r.PatientId, r.PatientName, r.PatientPhone, r.PatientDeleted),
                r.CreatedAt,
                r.UpdatedAt,
                RowVersionCodec.Encode(r.RowVersion)))
            .ToList();
    }

    public static string StatusText(AppointmentStatus status) => status switch
    {
        AppointmentStatus.Booked => "booked",
        AppointmentStatus.Completed => "completed",
        AppointmentStatus.Cancelled => "cancelled",
        AppointmentStatus.NoShow => "noShow",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };
}
