using System.Net;
using System.Text.Json;
using ClinicBooking.Domain.Permissions;
using ClinicBooking.Tests.Support;
using static ClinicBooking.Tests.Support.AppointmentHelpers;
using static ClinicBooking.Tests.Support.AuthHelpers;
using static ClinicBooking.Tests.Support.DoctorHelpers;
using static ClinicBooking.Tests.Support.PatientHelpers;
using static ClinicBooking.Tests.Support.SpecialtyHelpers;

namespace ClinicBooking.Tests;

/// <summary>
/// After booking (D41, D66): reschedule with every booking rule, cancel before the start, record Completed or
/// NoShow after it, final statuses, row versions, the patient-overlap warning and the day list.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class AppointmentsLifecycleTests : IClassFixture<AuthApiFixture>
{
    // Wednesday 1 July 2026, 09:00 in Cairo.
    private static readonly DateTimeOffset BaseTime = new(2026, 7, 1, 6, 0, 0, TimeSpan.Zero);

    private readonly AuthApiFixture _fixture;
    private readonly HttpClient _client;

    public AppointmentsLifecycleTests(AuthApiFixture fixture)
    {
        _fixture = fixture;
        _fixture.Clock.Now = BaseTime;
        _client = fixture.Factory.CreateClient();
    }

    /// <summary>Moves the clock and signs the desk in again (the old token's lifetime has passed).</summary>
    private async Task<Staff> AtAsync(DateTimeOffset now, Staff staff)
    {
        _fixture.Clock.Now = now;
        return await SignInAgainAsync(_client, staff);
    }

    [Fact]
    public async Task Rescheduling_moves_the_same_reference_to_a_new_slot_and_frees_the_old_one()
    {
        var w = await WorldAsync(_fixture, _client);
        var booked = await BookOkAsync(_client, w, Cairo(2026, 7, 2, 10, 0));

        var response = await RescheduleAsync(_client, w.Desk.Token, booked, Cairo(2026, 7, 3, 11, 0));

        await AssertStatusAsync(response, HttpStatusCode.OK);
        var moved = await ProblemAsync(response);
        Assert.Equal(Reference(booked), Reference(moved));
        Assert.Equal(Cairo(2026, 7, 3, 11, 0), moved.GetProperty("startUtc").GetDateTimeOffset());
        Assert.Equal(Cairo(2026, 7, 3, 11, 20), moved.GetProperty("endUtc").GetDateTimeOffset());
        Assert.NotEqual(booked.GetProperty("rowVersion").GetString(), moved.GetProperty("rowVersion").GetString());
        Assert.Contains(Cairo(2026, 7, 2, 10, 0), await SlotStartsAsync(_client, w.Desk.Token, w.Clinic, w.Doctor, "2026-07-02"));

        // The old row version is stale now.
        await AssertProblemAsync(
            await RescheduleAsync(_client, w.Desk.Token, booked, Cairo(2026, 7, 3, 9, 0)),
            HttpStatusCode.Conflict,
            "error.concurrency.conflict");
    }

    [Fact]
    public async Task Rescheduling_to_its_own_time_changes_nothing_and_never_counts_itself()
    {
        var w = await WorldAsync(_fixture, _client);
        var booked = await BookOkAsync(_client, w, Cairo(2026, 7, 2, 10, 0));

        var same = await RescheduleAsync(_client, w.Desk.Token, booked, Cairo(2026, 7, 2, 10, 0));

        await AssertStatusAsync(same, HttpStatusCode.OK);
        Assert.Equal(booked.GetProperty("rowVersion").GetString(), (await ProblemAsync(same)).GetProperty("rowVersion").GetString());

        // To the next slot: the appointment itself is neither a taken slot nor the patient's overlap.
        await AssertStatusAsync(await RescheduleAsync(_client, w.Desk.Token, booked, Cairo(2026, 7, 2, 10, 20)), HttpStatusCode.OK);
    }

    [Fact]
    public async Task Rescheduling_applies_every_booking_rule()
    {
        var w = await WorldAsync(_fixture, _client);
        var booked = await BookOkAsync(_client, w, Cairo(2026, 7, 2, 10, 0));
        var other = await BookOkAsync(_client, w.Desk.Token, w.Clinic, w.Doctor, await NewPatientAsync(_fixture, _client), Cairo(2026, 7, 2, 11, 0));

        async Task Expect(DateTimeOffset start, HttpStatusCode status, string key) =>
            await AssertProblemAsync(await RescheduleAsync(_client, w.Desk.Token, booked, start), status, key);

        await Expect(Cairo(2026, 7, 2, 11, 0), HttpStatusCode.Conflict, "error.appointment.slot_taken");
        await Expect(Cairo(2026, 7, 2, 10, 5), HttpStatusCode.UnprocessableEntity, "error.appointment.off_grid");
        await Expect(Cairo(2026, 6, 30, 10, 0), HttpStatusCode.UnprocessableEntity, "error.appointment.in_past");
        await Expect(Cairo(2026, 9, 30, 10, 0), HttpStatusCode.UnprocessableEntity, "error.appointment.too_far_ahead");

        await AssertStatusAsync(
            await SendAsync(_client, HttpMethod.Post, $"/api/doctors/{w.Doctor}/leave-days", w.Manager.Token, new { from = "2026-07-04", to = "2026-07-04" }),
            HttpStatusCode.OK);
        await Expect(Cairo(2026, 7, 4, 10, 0), HttpStatusCode.UnprocessableEntity, "error.appointment.on_leave");

        Assert.Equal("booked", Status(await GetAppointmentOkAsync(_client, w.Desk.Token, Reference(other))));
        Assert.Equal(400, (int)(await RescheduleAsync(_client, w.Desk.Token, booked, Cairo(2026, 7, 2, 10, 0), rowVersion: "")).StatusCode);
    }

    [Fact]
    public async Task Cancelling_is_allowed_only_before_the_start_and_is_final()
    {
        var w = await WorldAsync(_fixture, _client);
        var booked = await BookOkAsync(_client, w, Cairo(2026, 7, 2, 10, 0));
        var started = await BookOkAsync(_client, w.Desk.Token, w.Clinic, w.Doctor, await NewPatientAsync(_fixture, _client), Cairo(2026, 7, 1, 9, 20));

        var cancel = await ChangeStatusAsync(_client, w.Desk.Token, booked, "cancel");
        await AssertStatusAsync(cancel, HttpStatusCode.OK);
        var cancelled = await ProblemAsync(cancel);
        Assert.Equal("cancelled", Status(cancelled));

        // Final: no second cancel, no record, no reschedule.
        foreach (var action in new[] { "cancel", "complete", "no-show" })
        {
            await AssertProblemAsync(
                await ChangeStatusAsync(_client, w.Desk.Token, cancelled, action), HttpStatusCode.UnprocessableEntity, "error.appointment.not_booked");
        }

        await AssertProblemAsync(
            await RescheduleAsync(_client, w.Desk.Token, cancelled, Cairo(2026, 7, 2, 11, 0)),
            HttpStatusCode.UnprocessableEntity,
            "error.appointment.not_booked");

        // After its start an appointment can no longer be cancelled or moved.
        var desk = await AtAsync(Cairo(2026, 7, 1, 9, 25), w.Desk);
        await AssertProblemAsync(
            await ChangeStatusAsync(_client, desk.Token, started, "cancel"), HttpStatusCode.UnprocessableEntity, "error.appointment.already_started");
        await AssertProblemAsync(
            await RescheduleAsync(_client, desk.Token, started, Cairo(2026, 7, 2, 11, 0)),
            HttpStatusCode.UnprocessableEntity,
            "error.appointment.already_started");
    }

    [Fact]
    public async Task Recording_is_allowed_only_after_the_start_and_is_final()
    {
        var w = await WorldAsync(_fixture, _client);
        var first = await BookOkAsync(_client, w, Cairo(2026, 7, 1, 10, 0));
        var second = await BookOkAsync(_client, w.Desk.Token, w.Clinic, w.Doctor, await NewPatientAsync(_fixture, _client), Cairo(2026, 7, 1, 10, 20));

        await AssertProblemAsync(
            await ChangeStatusAsync(_client, w.Desk.Token, first, "complete"), HttpStatusCode.UnprocessableEntity, "error.appointment.not_started");
        await AssertProblemAsync(
            await ChangeStatusAsync(_client, w.Desk.Token, first, "no-show"), HttpStatusCode.UnprocessableEntity, "error.appointment.not_started");

        // At the start time exactly, recording is allowed.
        var desk = await AtAsync(Cairo(2026, 7, 1, 10, 0), w.Desk);
        var completed = await ProblemAsync(await ChangeStatusAsync(_client, desk.Token, first, "complete"));
        Assert.Equal("completed", Status(completed));

        desk = await AtAsync(Cairo(2026, 7, 1, 11, 0), desk);
        var noShow = await ProblemAsync(await ChangeStatusAsync(_client, desk.Token, second, "no-show"));
        Assert.Equal("noShow", Status(noShow));

        foreach (var (appointment, action) in new[] { (completed, "no-show"), (completed, "cancel"), (noShow, "complete"), (noShow, "cancel") })
        {
            await AssertProblemAsync(
                await ChangeStatusAsync(_client, desk.Token, appointment, action), HttpStatusCode.UnprocessableEntity, "error.appointment.not_booked");
        }

        // The day list shows both outcomes.
        Assert.Equal(["completed", "noShow"], (await DayListOkAsync(_client, desk.Token, w.Clinic, "2026-07-01")).EnumerateArray().Select(Status));
    }

    [Fact]
    public async Task A_stale_or_missing_row_version_is_refused()
    {
        var w = await WorldAsync(_fixture, _client);
        var booked = await BookOkAsync(_client, w, Cairo(2026, 7, 2, 10, 0));
        var moved = await ProblemAsync(await RescheduleAsync(_client, w.Desk.Token, booked, Cairo(2026, 7, 2, 10, 40)));

        await AssertProblemAsync(await ChangeStatusAsync(_client, w.Desk.Token, booked, "cancel"), HttpStatusCode.Conflict, "error.concurrency.conflict");
        Assert.Equal(
            "error.concurrency.row_version_required",
            await FieldErrorAsync(await ChangeStatusAsync(_client, w.Desk.Token, Reference(moved), "cancel", null), "rowVersion"));
        await AssertStatusAsync(await ChangeStatusAsync(_client, w.Desk.Token, moved, "cancel"), HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_unknown_or_malformed_reference_is_404()
    {
        var w = await WorldAsync(_fixture, _client);

        foreach (var reference in new[] { "ZZZZZZ", "A7K2M0", "short", "toolongvalue" })
        {
            await AssertProblemAsync(await GetAppointmentAsync(_client, w.Desk.Token, reference), HttpStatusCode.NotFound, "error.appointment.not_found");
            await AssertProblemAsync(
                await ChangeStatusAsync(_client, w.Desk.Token, reference, "cancel", "AAAAAAAAAAA="), HttpStatusCode.NotFound, "error.appointment.not_found");
        }
    }

    [Fact]
    public async Task An_overlapping_appointment_of_the_patient_is_a_warning_with_the_matches()
    {
        // The patient has 10:00–10:20 with doctor A in clinic A.
        var w = await WorldAsync(_fixture, _client);
        await BookOkAsync(_client, w, Cairo(2026, 7, 2, 10, 0));

        // Doctor B in clinic B, 30-minute slots from 09:45: 09:45–10:15 overlaps.
        var clinicB = await NewClinicAsync(_fixture, _client);
        var doctorB = await NewDoctorAsync(_fixture, _client, [clinicB], 30);
        var managerB = await StaffAsync(_fixture, _client, [clinicB], [Permissions.Doctors.Manage]);
        await AssertStatusAsync(await SaveHoursAsync(_client, managerB.Token, doctorB, clinicB, EveryDay("09:45", "12:00")), HttpStatusCode.OK);

        // A desk in clinic B that may read appointments in clinic A too sees the match.
        var deskB = await StaffAsync(_fixture, _client, [clinicB], AllAppointmentPermissions, Permissions.Patients.Read);
        await GrantAsync(_fixture.Factory.Services, deskB.UserId, w.Clinic, Permissions.Appointments.Read);

        var response = await BookAsync(_client, deskB.Token, clinicB, doctorB, w.Patient, Cairo(2026, 7, 2, 9, 45));

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "error.appointment.patient_overlap");
        var problem = await ProblemAsync(response);
        Assert.Equal(1, problem.GetProperty("matchCount").GetInt32());
        var match = Assert.Single(problem.GetProperty("matches").EnumerateArray());
        Assert.Equal(Cairo(2026, 7, 2, 10, 0), match.GetProperty("startUtc").GetDateTimeOffset());
        Assert.Equal(Cairo(2026, 7, 2, 10, 20), match.GetProperty("endUtc").GetDateTimeOffset());
        Assert.Equal(w.Doctor, match.GetProperty("doctor").GetProperty("id").GetInt64());
        Assert.Equal(w.Clinic, match.GetProperty("clinic").GetProperty("id").GetInt64());
        Assert.False(problem.GetRawText().Contains("phone", StringComparison.OrdinalIgnoreCase));

        // Nothing was saved; with the confirmation it is.
        Assert.Empty((await DayListOkAsync(_client, deskB.Token, clinicB, "2026-07-02")).EnumerateArray());
        await BookOkAsync(_client, deskB.Token, clinicB, doctorB, w.Patient, Cairo(2026, 7, 2, 9, 45), confirmPatientOverlap: true);

        // 10:45–11:15 overlaps neither 10:00–10:20 nor 09:45–10:15: no warning.
        await BookOkAsync(_client, deskB.Token, clinicB, doctorB, w.Patient, Cairo(2026, 7, 2, 10, 45));
    }

    [Fact]
    public async Task Without_appointments_read_in_the_matching_clinic_only_the_count_is_given()
    {
        var w = await WorldAsync(_fixture, _client);
        await BookOkAsync(_client, w, Cairo(2026, 7, 2, 10, 0));

        var clinicB = await NewClinicAsync(_fixture, _client);
        var doctorB = await NewDoctorAsync(_fixture, _client, [clinicB]);
        var managerB = await StaffAsync(_fixture, _client, [clinicB], [Permissions.Doctors.Manage]);
        await AssertStatusAsync(await SaveHoursAsync(_client, managerB.Token, doctorB, clinicB, EveryDay()), HttpStatusCode.OK);
        var bookerB = await StaffAsync(_fixture, _client, [clinicB], [Permissions.Appointments.Book]);

        var response = await BookAsync(_client, bookerB.Token, clinicB, doctorB, w.Patient, Cairo(2026, 7, 2, 10, 0));

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "error.appointment.patient_overlap");
        var problem = await ProblemAsync(response);
        Assert.Equal(1, problem.GetProperty("matchCount").GetInt32());
        Assert.False(problem.TryGetProperty("matches", out _));
    }

    [Fact]
    public async Task Rescheduling_warns_about_the_patients_overlaps_too()
    {
        var w = await WorldAsync(_fixture, _client);
        var clinicB = await NewClinicAsync(_fixture, _client);
        var doctorB = await NewDoctorAsync(_fixture, _client, [clinicB]);
        var managerB = await StaffAsync(_fixture, _client, [clinicB], [Permissions.Doctors.Manage]);
        await AssertStatusAsync(await SaveHoursAsync(_client, managerB.Token, doctorB, clinicB, EveryDay()), HttpStatusCode.OK);
        var deskB = await StaffAsync(_fixture, _client, [clinicB], AllAppointmentPermissions);

        await BookOkAsync(_client, deskB.Token, clinicB, doctorB, w.Patient, Cairo(2026, 7, 2, 11, 0));
        var booked = await BookOkAsync(_client, w, Cairo(2026, 7, 2, 10, 0));

        var response = await RescheduleAsync(_client, w.Desk.Token, booked, Cairo(2026, 7, 2, 11, 0));
        await AssertProblemAsync(response, HttpStatusCode.Conflict, "error.appointment.patient_overlap");
        Assert.Equal(Cairo(2026, 7, 2, 10, 0), (await GetAppointmentOkAsync(_client, w.Desk.Token, Reference(booked))).GetProperty("startUtc").GetDateTimeOffset());

        await AssertStatusAsync(
            await RescheduleAsync(_client, w.Desk.Token, booked, Cairo(2026, 7, 2, 11, 0), confirmPatientOverlap: true), HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_day_list_shows_every_status_in_start_order_optionally_for_one_doctor()
    {
        var w = await WorldAsync(_fixture, _client);
        var doctor2 = await NewDoctorAsync(_fixture, _client, [w.Clinic]);
        await AssertStatusAsync(await SaveHoursAsync(_client, w.Manager.Token, doctor2, w.Clinic, EveryDay()), HttpStatusCode.OK);
        var p2 = await NewPatientAsync(_fixture, _client);

        var late = await BookOkAsync(_client, w, Cairo(2026, 7, 2, 11, 0));
        var early = await BookOkAsync(_client, w.Desk.Token, w.Clinic, doctor2, p2, Cairo(2026, 7, 2, 9, 0));
        var cancelled = await BookOkAsync(_client, w.Desk.Token, w.Clinic, w.Doctor, p2, Cairo(2026, 7, 2, 10, 0));
        await AssertStatusAsync(await ChangeStatusAsync(_client, w.Desk.Token, cancelled, "cancel"), HttpStatusCode.OK);
        await BookOkAsync(_client, w, Cairo(2026, 7, 3, 9, 0)); // another day

        var all = await DayListOkAsync(_client, w.Desk.Token, w.Clinic, "2026-07-02");
        Assert.Equal([Reference(early), Reference(cancelled), Reference(late)], References(all));
        Assert.Equal(["booked", "cancelled", "booked"], all.EnumerateArray().Select(Status));

        var one = await DayListOkAsync(_client, w.Desk.Token, w.Clinic, "2026-07-02", w.Doctor);
        Assert.Equal([Reference(cancelled), Reference(late)], References(one));

        Assert.Equal(
            "error.appointment.date_required",
            await FieldErrorAsync(await SendAsync(_client, HttpMethod.Get, $"/api/clinics/{w.Clinic}/appointments", w.Desk.Token), "date"));
    }

    [Fact]
    public async Task Patient_name_and_phone_go_only_to_a_caller_with_patients_read()
    {
        var w = await WorldAsync(_fixture, _client);
        var booked = await BookOkAsync(_client, w, Cairo(2026, 7, 2, 10, 0));
        var reader = await StaffAsync(_fixture, _client, [w.Clinic], [Permissions.Appointments.Read]);

        var withRead = (await DayListOkAsync(_client, w.Desk.Token, w.Clinic, "2026-07-02"))[0].GetProperty("patient");
        Assert.NotNull(withRead.GetProperty("name").GetString());
        Assert.StartsWith("+20", withRead.GetProperty("phone").GetString());

        foreach (var patient in new[]
                 {
                     (await DayListOkAsync(_client, reader.Token, w.Clinic, "2026-07-02"))[0].GetProperty("patient"),
                     (await GetAppointmentOkAsync(_client, reader.Token, Reference(booked))).GetProperty("patient"),
                 })
        {
            Assert.Equal(w.Patient, patient.GetProperty("id").GetInt64());
            Assert.Equal(JsonValueKind.Null, patient.GetProperty("name").ValueKind);
            Assert.Equal(JsonValueKind.Null, patient.GetProperty("phone").ValueKind);
        }
    }

    [Fact]
    public async Task A_deleted_doctor_or_patient_still_shows_flagged_in_the_history()
    {
        var w = await WorldAsync(_fixture, _client);
        var past = await BookOkAsync(_client, w, Cairo(2026, 7, 1, 10, 0));

        // Later the patient and the doctor are deleted; the past appointment stays booked (unrecorded) and visible.
        var desk = await AtAsync(Cairo(2026, 7, 1, 11, 0), w.Desk);
        var patients = await PatientManagerAsync(_fixture, _client);
        await AssertStatusAsync(await SendAsync(_client, HttpMethod.Delete, $"/api/patients/{w.Patient}", patients), HttpStatusCode.NoContent);
        var manager = await SignInAgainAsync(_client, w.Manager);
        await AssertStatusAsync(await SendAsync(_client, HttpMethod.Delete, $"/api/doctors/{w.Doctor}", manager.Token), HttpStatusCode.NoContent);

        var row = Assert.Single((await DayListOkAsync(_client, desk.Token, w.Clinic, "2026-07-01")).EnumerateArray());
        Assert.Equal(Reference(past), Reference(row));
        Assert.True(row.GetProperty("doctor").GetProperty("isDeleted").GetBoolean());
        Assert.True(row.GetProperty("patient").GetProperty("isDeleted").GetBoolean());
        Assert.NotNull(row.GetProperty("patient").GetProperty("name").GetString());

        var read = await GetAppointmentOkAsync(_client, desk.Token, Reference(past));
        Assert.True(read.GetProperty("doctor").GetProperty("isDeleted").GetBoolean());

        // It can still be recorded.
        Assert.Equal("noShow", Status(await ProblemAsync(await ChangeStatusAsync(_client, desk.Token, read, "no-show"))));
    }
}
