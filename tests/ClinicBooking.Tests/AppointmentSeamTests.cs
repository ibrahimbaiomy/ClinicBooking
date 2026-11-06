using System.Net;
using ClinicBooking.Domain.Permissions;
using ClinicBooking.Tests.Support;
using static ClinicBooking.Tests.Support.AppointmentHelpers;
using static ClinicBooking.Tests.Support.AuthHelpers;
using static ClinicBooking.Tests.Support.DoctorHelpers;
using static ClinicBooking.Tests.Support.PatientHelpers;

namespace ClinicBooking.Tests;

/// <summary>
/// The Phase 2 seams filled by appointments (D35, D43, D61, D63, D66): deactivating an assignment, deleting a
/// doctor or a patient and adding leave count the upcoming appointments and cancel them only once confirmed; a
/// slot change and new working hours never strand a booked appointment. Leave days themselves.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class AppointmentSeamTests : IClassFixture<AuthApiFixture>
{
    // Wednesday 1 July 2026, 09:00 in Cairo.
    private static readonly DateTimeOffset BaseTime = new(2026, 7, 1, 6, 0, 0, TimeSpan.Zero);

    private readonly AuthApiFixture _fixture;
    private readonly HttpClient _client;

    public AppointmentSeamTests(AuthApiFixture fixture)
    {
        _fixture = fixture;
        _fixture.Clock.Now = BaseTime;
        _client = fixture.Factory.CreateClient();
    }

    private async Task<string> StatusOfAsync(World w, string reference) =>
        Status(await GetAppointmentOkAsync(_client, (await SignInAgainAsync(_client, w.Desk)).Token, reference));

    /// <summary>A booked appointment whose start has passed by the time the test acts (09:20 today; acting at 09:30).</summary>
    private async Task<string> StartedAsync(World w) =>
        Reference(await BookOkAsync(_client, w.Desk.Token, w.Clinic, w.Doctor, await NewPatientAsync(_fixture, _client), Cairo(2026, 7, 1, 9, 20)));

    private async Task<string[]> LeaveDatesAsync(long doctorId, string token, string from, string to)
    {
        var response = await SendAsync(_client, HttpMethod.Get, $"/api/doctors/{doctorId}/leave-days?from={from}&to={to}", token);
        await AssertStatusAsync(response, HttpStatusCode.OK);
        return (await ProblemAsync(response)).GetProperty("dates").EnumerateArray().Select(d => d.GetString()!).ToArray();
    }

    private async Task<Staff> At0930Async(Staff staff)
    {
        _fixture.Clock.Now = Cairo(2026, 7, 1, 9, 30);
        return await SignInAgainAsync(_client, staff);
    }

    [Fact]
    public async Task Deactivating_with_upcoming_appointments_there_asks_then_cancels_them()
    {
        var w = await WorldAsync(_fixture, _client);
        var started = await StartedAsync(w);
        var a = Reference(await BookOkAsync(_client, w, Cairo(2026, 7, 2, 10, 0)));
        var b = Reference(await BookOkAsync(_client, w, Cairo(2026, 7, 3, 10, 0)));

        // The same doctor in a second clinic keeps its appointment there.
        var clinic2 = await NewClinicAsync(_fixture, _client);
        var manager = await At0930Async(w.Manager);
        await GrantAsync(_fixture.Factory.Services, manager.UserId, clinic2, Permissions.Doctors.Manage);
        await AssertStatusAsync(await SendAsync(_client, HttpMethod.Post, $"/api/doctors/{w.Doctor}/clinics/{clinic2}", manager.Token), HttpStatusCode.OK);
        await AssertStatusAsync(await SaveHoursAsync(_client, manager.Token, w.Doctor, clinic2, Period(4, "13:00", "15:00")), HttpStatusCode.OK);
        var desk2 = await StaffAsync(_fixture, _client, [clinic2], [Permissions.Appointments.Book, Permissions.Appointments.Read]);
        var elsewhere = Reference(await BookOkAsync(_client, desk2.Token, clinic2, w.Doctor, w.Patient, Cairo(2026, 7, 2, 13, 0)));

        var url = $"/api/doctors/{w.Doctor}/clinics/{w.Clinic}/deactivate";
        var refused = await SendAsync(_client, HttpMethod.Post, url, manager.Token);
        await AssertProblemAsync(refused, HttpStatusCode.Conflict, "error.doctor.has_future_appointments");
        Assert.Equal(2, await AppointmentCountAsync(refused));
        Assert.True(Assignments(await GetDoctorAsync(_client, manager.Token, w.Doctor)).Single(c => c.ClinicId == w.Clinic).IsActive);

        await AssertStatusAsync(
            await SendAsync(_client, HttpMethod.Post, url, manager.Token, new { confirmCancelAppointments = true }), HttpStatusCode.OK);

        Assert.Equal("cancelled", await StatusOfAsync(w, a));
        Assert.Equal("cancelled", await StatusOfAsync(w, b));
        Assert.Equal("booked", await StatusOfAsync(w, started)); // started: to be recorded
        Assert.Equal("booked", Status(await GetAppointmentOkAsync(_client, (await SignInAgainAsync(_client, desk2)).Token, elsewhere)));
    }

    [Fact]
    public async Task Deactivating_without_upcoming_appointments_needs_no_confirmation()
    {
        var w = await WorldAsync(_fixture, _client);
        await StartedAsync(w);
        var manager = await At0930Async(w.Manager);

        await AssertStatusAsync(
            await SendAsync(_client, HttpMethod.Post, $"/api/doctors/{w.Doctor}/clinics/{w.Clinic}/deactivate", manager.Token), HttpStatusCode.OK);
    }

    [Fact]
    public async Task Deleting_a_doctor_with_upcoming_appointments_asks_then_cancels_them_in_every_clinic()
    {
        var w = await WorldAsync(_fixture, _client);
        var started = await StartedAsync(w);
        var a = Reference(await BookOkAsync(_client, w, Cairo(2026, 7, 2, 10, 0)));
        var manager = await At0930Async(w.Manager);

        var refused = await SendAsync(_client, HttpMethod.Delete, $"/api/doctors/{w.Doctor}", manager.Token);
        await AssertProblemAsync(refused, HttpStatusCode.Conflict, "error.doctor.has_future_appointments");
        Assert.Equal(1, await AppointmentCountAsync(refused));
        await GetDoctorAsync(_client, manager.Token, w.Doctor); // still there

        await AssertStatusAsync(
            await SendAsync(_client, HttpMethod.Delete, $"/api/doctors/{w.Doctor}?confirmCancelAppointments=true", manager.Token),
            HttpStatusCode.NoContent);

        Assert.Equal("cancelled", await StatusOfAsync(w, a));
        Assert.Equal("booked", await StatusOfAsync(w, started));
    }

    [Fact]
    public async Task Deleting_a_patient_with_upcoming_appointments_asks_then_cancels_them()
    {
        var w = await WorldAsync(_fixture, _client);
        var a = Reference(await BookOkAsync(_client, w, Cairo(2026, 7, 2, 10, 0)));
        var b = Reference(await BookOkAsync(_client, w, Cairo(2026, 7, 5, 10, 0)));
        var patients = await PatientManagerAsync(_fixture, _client);

        var refused = await SendAsync(_client, HttpMethod.Delete, $"/api/patients/{w.Patient}", patients);
        await AssertProblemAsync(refused, HttpStatusCode.Conflict, "error.patient.has_future_appointments");
        Assert.Equal(2, await AppointmentCountAsync(refused));

        await AssertStatusAsync(
            await SendAsync(_client, HttpMethod.Delete, $"/api/patients/{w.Patient}?confirmCancelAppointments=true", patients),
            HttpStatusCode.NoContent);
        Assert.Equal("cancelled", await StatusOfAsync(w, a));
        Assert.Equal("cancelled", await StatusOfAsync(w, b));
    }

    [Fact]
    public async Task Leave_over_a_range_asks_once_with_the_total_then_cancels_and_skips_existing_days()
    {
        var w = await WorldAsync(_fixture, _client);
        var started = await StartedAsync(w);
        var today = Reference(await BookOkAsync(_client, w, Cairo(2026, 7, 1, 11, 0)));
        var a = Reference(await BookOkAsync(_client, w, Cairo(2026, 7, 2, 10, 0)));
        var b = Reference(await BookOkAsync(_client, w, Cairo(2026, 7, 4, 10, 0)));
        var outside = Reference(await BookOkAsync(_client, w, Cairo(2026, 7, 6, 10, 0)));
        var manager = await At0930Async(w.Manager);
        var url = $"/api/doctors/{w.Doctor}/leave-days";

        // 3 July is already on leave (nothing booked there): skipped.
        await AssertStatusAsync(await SendAsync(_client, HttpMethod.Post, url, manager.Token, new { from = "2026-07-03", to = "2026-07-03" }), HttpStatusCode.OK);

        var refused = await SendAsync(_client, HttpMethod.Post, url, manager.Token, new { from = "2026-07-01", to = "2026-07-05" });
        await AssertProblemAsync(refused, HttpStatusCode.Conflict, "error.doctor.leave_has_appointments");
        Assert.Equal(3, await AppointmentCountAsync(refused)); // today 11:00, 2 July, 4 July; not the started one
        Assert.Equal(["2026-07-03"], await LeaveDatesAsync(w.Doctor, manager.Token, "2026-07-01", "2026-07-31"));

        var confirmed = await SendAsync(_client, HttpMethod.Post, url, manager.Token, new { from = "2026-07-01", to = "2026-07-05", confirmCancelAppointments = true });
        await AssertStatusAsync(confirmed, HttpStatusCode.OK);
        Assert.Equal(
            ["2026-07-01", "2026-07-02", "2026-07-03", "2026-07-04", "2026-07-05"],
            (await ProblemAsync(confirmed)).GetProperty("dates").EnumerateArray().Select(d => d.GetString()));

        Assert.Equal("cancelled", await StatusOfAsync(w, today));
        Assert.Equal("cancelled", await StatusOfAsync(w, a));
        Assert.Equal("cancelled", await StatusOfAsync(w, b));
        Assert.Equal("booked", await StatusOfAsync(w, started));
        Assert.Equal("booked", await StatusOfAsync(w, outside));

        // Again over the same days: everything already on leave, nothing to confirm.
        await AssertStatusAsync(await SendAsync(_client, HttpMethod.Post, url, manager.Token, new { from = "2026-07-01", to = "2026-07-05" }), HttpStatusCode.OK);
    }

    [Fact]
    public async Task Leave_applies_in_every_clinic_even_one_the_caller_does_not_manage()
    {
        var w = await WorldAsync(_fixture, _client);
        var clinic2 = await NewClinicAsync(_fixture, _client);
        var both = await StaffAsync(_fixture, _client, [clinic2], [Permissions.Doctors.Manage]);
        await GrantAsync(_fixture.Factory.Services, both.UserId, w.Clinic, Permissions.Doctors.Manage);
        await AssertStatusAsync(await SendAsync(_client, HttpMethod.Post, $"/api/doctors/{w.Doctor}/clinics/{clinic2}", both.Token), HttpStatusCode.OK);
        await AssertStatusAsync(await SaveHoursAsync(_client, both.Token, w.Doctor, clinic2, Period(4, "13:00", "15:00")), HttpStatusCode.OK);
        var desk2 = await StaffAsync(_fixture, _client, [clinic2], [Permissions.Appointments.Book, Permissions.Appointments.Read]);
        var there = Reference(await BookOkAsync(_client, desk2.Token, clinic2, w.Doctor, w.Patient, Cairo(2026, 7, 2, 13, 0)));

        // w.Manager manages only the first clinic.
        var response = await SendAsync(
            _client, HttpMethod.Post, $"/api/doctors/{w.Doctor}/leave-days", w.Manager.Token, new { from = "2026-07-02", to = "2026-07-02", confirmCancelAppointments = true });

        await AssertStatusAsync(response, HttpStatusCode.OK);
        Assert.Equal("cancelled", Status(await GetAppointmentOkAsync(_client, desk2.Token, there)));
        Assert.Empty(await SlotStartsAsync(_client, desk2.Token, clinic2, w.Doctor, "2026-07-02"));
    }

    [Fact]
    public async Task Leave_rules_validation_and_removal()
    {
        var w = await WorldAsync(_fixture, _client);
        var url = $"/api/doctors/{w.Doctor}/leave-days";

        await AssertProblemAsync(
            await SendAsync(_client, HttpMethod.Post, url, w.Manager.Token, new { from = "2026-06-30", to = "2026-07-02" }),
            HttpStatusCode.UnprocessableEntity,
            "error.doctor.leave_in_past");
        Assert.Equal(
            "error.doctor.leave_range_invalid",
            await FieldErrorAsync(await SendAsync(_client, HttpMethod.Post, url, w.Manager.Token, new { from = "2026-07-05", to = "2026-07-04" }), "to"));
        Assert.Equal(
            "error.doctor.leave_range_too_long",
            await FieldErrorAsync(await SendAsync(_client, HttpMethod.Post, url, w.Manager.Token, new { from = "2026-07-01", to = "2026-08-30" }), "to"));
        Assert.Equal(
            "error.doctor.leave_from_required",
            await FieldErrorAsync(await SendAsync(_client, HttpMethod.Post, url, w.Manager.Token, new { to = "2026-07-04" }), "from"));
        Assert.Equal(
            "error.doctor.leave_to_required",
            await FieldErrorAsync(await SendAsync(_client, HttpMethod.Post, url, w.Manager.Token, new { from = "2026-07-04" }), "to"));

        // 60 days, both ends included, is the limit.
        await AssertStatusAsync(await SendAsync(_client, HttpMethod.Post, url, w.Manager.Token, new { from = "2026-07-01", to = "2026-08-29" }), HttpStatusCode.OK);
        Assert.Equal(60, (await LeaveDatesAsync(w.Doctor, w.Manager.Token, "2026-07-01", "2026-12-31")).Length);

        // Removing one day, today or later; a day that is not leave is 404; a past day is 422.
        await AssertStatusAsync(await SendAsync(_client, HttpMethod.Delete, $"{url}/2026-07-10", w.Manager.Token), HttpStatusCode.NoContent);
        await AssertProblemAsync(await SendAsync(_client, HttpMethod.Delete, $"{url}/2026-07-10", w.Manager.Token), HttpStatusCode.NotFound, "error.doctor.leave_day_not_found");
        Assert.NotEmpty(await SlotStartsAsync(_client, w.Desk.Token, w.Clinic, w.Doctor, "2026-07-10"));

        _fixture.Clock.Now = Cairo(2026, 7, 3, 9, 0);
        var manager = await SignInAgainAsync(_client, w.Manager);
        await AssertProblemAsync(await SendAsync(_client, HttpMethod.Delete, $"{url}/2026-07-02", manager.Token), HttpStatusCode.UnprocessableEntity, "error.doctor.leave_in_past");
        await AssertStatusAsync(await SendAsync(_client, HttpMethod.Delete, $"{url}/2026-07-03", manager.Token), HttpStatusCode.NoContent);

        // Reading: any signed-in user; at most 366 days.
        var (reader, _) = await SpecialtyHelpers.SignInAsync(_fixture, _client);
        Assert.Contains("2026-07-01", await LeaveDatesAsync(w.Doctor, reader, "2026-07-01", "2027-07-01"));
        Assert.Equal(
            "error.doctor.leave_range_too_long",
            await FieldErrorAsync(await SendAsync(_client, HttpMethod.Get, $"{url}?from=2026-07-01&to=2027-07-02", reader), "to"));
        await AssertProblemAsync(
            await SendAsync(_client, HttpMethod.Get, "/api/doctors/999999/leave-days?from=2026-07-01&to=2026-07-02", reader), HttpStatusCode.NotFound, "error.doctor.not_found");
    }

    [Fact]
    public async Task Leave_needs_doctors_manage_in_any_of_the_doctors_clinics()
    {
        var w = await WorldAsync(_fixture, _client);
        var url = $"/api/doctors/{w.Doctor}/leave-days";
        var body = new { from = "2026-07-02", to = "2026-07-02" };

        // Nothing in the doctor's clinics: 404; another clinic-scoped permission there: 403.
        var (stranger, _) = await SpecialtyHelpers.SignInAsync(_fixture, _client);
        await AssertProblemAsync(await SendAsync(_client, HttpMethod.Post, url, stranger, body), HttpStatusCode.NotFound, "error.doctor.not_found");
        await AssertProblemAsync(await SendAsync(_client, HttpMethod.Delete, $"{url}/2026-07-02", stranger), HttpStatusCode.NotFound, "error.doctor.not_found");
        await AssertProblemAsync(await SendAsync(_client, HttpMethod.Post, url, w.Desk.Token, body), HttpStatusCode.Forbidden, "error.auth.forbidden");
    }

    [Fact]
    public async Task A_slot_change_must_start_after_the_last_booked_appointment()
    {
        var w = await WorldAsync(_fixture, _client);
        await BookOkAsync(_client, w, Cairo(2026, 7, 9, 10, 0));
        var url = $"/api/doctors/{w.Doctor}/slot-durations";

        await AssertProblemAsync(
            await SendAsync(_client, HttpMethod.Post, url, w.Manager.Token, new { slotMinutes = 30, effectiveFrom = "2026-07-09" }),
            HttpStatusCode.UnprocessableEntity,
            "error.doctor.effective_from_before_last_appointment");
        await AssertStatusAsync(
            await SendAsync(_client, HttpMethod.Post, url, w.Manager.Token, new { slotMinutes = 30, effectiveFrom = "2026-07-10" }), HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_cancelled_appointment_does_not_hold_back_a_slot_change()
    {
        var w = await WorldAsync(_fixture, _client);
        var booked = await BookOkAsync(_client, w, Cairo(2026, 7, 20, 10, 0));
        await AssertStatusAsync(await ChangeStatusAsync(_client, w.Desk.Token, booked, "cancel"), HttpStatusCode.OK);

        await AssertStatusAsync(
            await SendAsync(_client, HttpMethod.Post, $"/api/doctors/{w.Doctor}/slot-durations", w.Manager.Token, new { slotMinutes = 30, effectiveFrom = "2026-07-02" }),
            HttpStatusCode.OK);
    }

    [Fact]
    public async Task New_hours_that_would_strand_an_upcoming_appointment_are_refused_with_the_count()
    {
        var w = await WorldAsync(_fixture, _client);
        await BookOkAsync(_client, w, Cairo(2026, 7, 2, 11, 0)); // Thursday
        await BookOkAsync(_client, w.Desk.Token, w.Clinic, w.Doctor, await NewPatientAsync(_fixture, _client), Cairo(2026, 7, 9, 9, 20));
        var cancelled = await BookOkAsync(_client, w, Cairo(2026, 7, 3, 9, 0));
        await AssertStatusAsync(await ChangeStatusAsync(_client, w.Desk.Token, cancelled, "cancel"), HttpStatusCode.OK);

        // Thursday ends at 11:00: the 2 July 11:00 appointment falls outside; a grid from 09:10 moves 9 July 09:20 off it.
        var outside = await SaveHoursAsync(_client, w.Manager.Token, w.Doctor, w.Clinic, Period(4, "09:10", "11:00"));
        await AssertProblemAsync(outside, HttpStatusCode.UnprocessableEntity, "error.doctor.hours_strand_appointments");
        Assert.Equal(2, await AppointmentCountAsync(outside));

        // Hours that keep both (Thursday 09:00–12:00 only): fine; Friday's cancelled one does not count.
        await AssertStatusAsync(await SaveHoursAsync(_client, w.Manager.Token, w.Doctor, w.Clinic, Period(4, "09:00", "12:00")), HttpStatusCode.OK);
    }
}
