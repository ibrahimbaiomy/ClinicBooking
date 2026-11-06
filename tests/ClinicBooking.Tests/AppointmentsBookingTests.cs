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
/// Booking rules (D41, D43, D66): an active assignment, a future start at most 90 days ahead, not on leave, on the
/// grid of that Cairo date with that date's duration, a free slot, a live patient; the free-slots endpoint; Cairo
/// dates around midnight and across daylight saving.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class AppointmentsBookingTests : IClassFixture<AuthApiFixture>
{
    // Wednesday 1 July 2026, 09:00 in Cairo (summer time, +3).
    private static readonly DateTimeOffset BaseTime = new(2026, 7, 1, 6, 0, 0, TimeSpan.Zero);

    private readonly AuthApiFixture _fixture;
    private readonly HttpClient _client;

    public AppointmentsBookingTests(AuthApiFixture fixture)
    {
        _fixture = fixture;
        _fixture.Clock.Now = BaseTime;
        _client = fixture.Factory.CreateClient();
    }

    [Fact]
    public async Task A_booking_answers_201_with_its_reference_and_ends_one_slot_later()
    {
        var w = await WorldAsync(_fixture, _client);
        var start = Cairo(2026, 7, 2, 10, 0);

        var response = await BookAsync(_client, w.Desk.Token, w.Clinic, w.Doctor, w.Patient, start);

        await AssertStatusAsync(response, HttpStatusCode.Created);
        var body = await ProblemAsync(response);
        var reference = Reference(body);
        Assert.Matches("^[2-9A-HJKMNP-Z]{6}$", reference);
        Assert.Equal($"/api/appointments/{reference}", response.Headers.Location!.AbsolutePath);
        Assert.Equal("booked", Status(body));
        Assert.Equal(start, body.GetProperty("startUtc").GetDateTimeOffset());
        Assert.Equal(start.AddMinutes(20), body.GetProperty("endUtc").GetDateTimeOffset());
        Assert.Equal(w.Doctor, body.GetProperty("doctor").GetProperty("id").GetInt64());
        Assert.False(body.GetProperty("doctor").GetProperty("isDeleted").GetBoolean());
        Assert.Equal(w.Clinic, body.GetProperty("clinic").GetProperty("id").GetInt64());
        Assert.Equal(w.Patient, body.GetProperty("patient").GetProperty("id").GetInt64());
        Assert.NotNull(body.GetProperty("patient").GetProperty("name").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("rowVersion").GetString()));

        // Read back by the reference, also typed in lower case.
        var read = await GetAppointmentOkAsync(_client, w.Desk.Token, reference.ToLowerInvariant());
        Assert.Equal(reference, Reference(read));
    }

    [Fact]
    public async Task Missing_fields_are_400_with_their_keys()
    {
        var w = await WorldAsync(_fixture, _client);

        var response = await SendAsync(_client, HttpMethod.Post, $"/api/clinics/{w.Clinic}/appointments", w.Desk.Token, new { });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "error.validation.failed");
        var errors = (await ProblemAsync(response)).GetProperty("errors");
        Assert.Equal("error.appointment.doctor_required", errors.GetProperty("doctorId")[0].GetString());
        Assert.Equal("error.appointment.patient_required", errors.GetProperty("patientId")[0].GetString());
        Assert.Equal("error.appointment.start_required", errors.GetProperty("startUtc")[0].GetString());
    }

    [Fact]
    public async Task An_unknown_or_deleted_patient_is_400_on_the_field()
    {
        var w = await WorldAsync(_fixture, _client);
        var start = Cairo(2026, 7, 2, 10, 0);

        Assert.Equal(
            "error.appointment.patient_unavailable",
            await FieldErrorAsync(await BookAsync(_client, w.Desk.Token, w.Clinic, w.Doctor, 999_999, start), "patientId"));

        var token = await PatientManagerAsync(_fixture, _client);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(_client, HttpMethod.Delete, $"/api/patients/{w.Patient}", token)).StatusCode);
        Assert.Equal(
            "error.appointment.patient_unavailable",
            await FieldErrorAsync(await BookAsync(_client, w.Desk.Token, w.Clinic, w.Doctor, w.Patient, start), "patientId"));
    }

    [Fact]
    public async Task A_doctor_not_active_in_the_clinic_cannot_be_booked()
    {
        var w = await WorldAsync(_fixture, _client);
        var start = Cairo(2026, 7, 2, 10, 0);

        // Another clinic where the desk may book, but the doctor does not work there.
        var other = await NewClinicAsync(_fixture, _client);
        await GrantAsync(_fixture.Factory.Services, w.Desk.UserId, other, Permissions.Appointments.Book);
        await AssertProblemAsync(
            await BookAsync(_client, w.Desk.Token, other, w.Doctor, w.Patient, start),
            HttpStatusCode.UnprocessableEntity,
            "error.appointment.doctor_not_available");

        // The assignment deactivated.
        await SetAssignmentActiveAsync(_fixture.Factory.Services, w.Doctor, w.Clinic, false);
        await AssertProblemAsync(
            await BookAsync(_client, w.Desk.Token, w.Clinic, w.Doctor, w.Patient, start),
            HttpStatusCode.UnprocessableEntity,
            "error.appointment.doctor_not_available");

        // An unknown doctor.
        await AssertProblemAsync(
            await BookAsync(_client, w.Desk.Token, w.Clinic, 999_999, w.Patient, start),
            HttpStatusCode.UnprocessableEntity,
            "error.appointment.doctor_not_available");
    }

    [Fact]
    public async Task The_start_must_be_in_the_future()
    {
        var w = await WorldAsync(_fixture, _client);

        // 09:00 today is exactly now; 09:20 is the next slot and is fine.
        await AssertProblemAsync(
            await BookAsync(_client, w.Desk.Token, w.Clinic, w.Doctor, w.Patient, Cairo(2026, 7, 1, 9, 0)),
            HttpStatusCode.UnprocessableEntity,
            "error.appointment.in_past");
        await AssertProblemAsync(
            await BookAsync(_client, w.Desk.Token, w.Clinic, w.Doctor, w.Patient, Cairo(2026, 6, 30, 10, 0)),
            HttpStatusCode.UnprocessableEntity,
            "error.appointment.in_past");
        await BookOkAsync(_client, w, Cairo(2026, 7, 1, 9, 20));
    }

    [Fact]
    public async Task At_most_90_days_ahead_by_Cairo_date()
    {
        var w = await WorldAsync(_fixture, _client);

        // 1 July + 90 days = 29 September.
        await BookOkAsync(_client, w, Cairo(2026, 9, 29, 11, 40));
        await AssertProblemAsync(
            await BookAsync(_client, w.Desk.Token, w.Clinic, w.Doctor, w.Patient, Cairo(2026, 9, 30, 9, 0)),
            HttpStatusCode.UnprocessableEntity,
            "error.appointment.too_far_ahead");
    }

    [Fact]
    public async Task The_start_must_be_on_the_grid_and_the_slot_must_end_inside_the_period()
    {
        var w = await WorldAsync(_fixture, _client);

        foreach (var start in new[] { Cairo(2026, 7, 2, 10, 10), Cairo(2026, 7, 2, 11, 50), Cairo(2026, 7, 2, 8, 40), Cairo(2026, 7, 2, 12, 0) })
        {
            await AssertProblemAsync(
                await BookAsync(_client, w.Desk.Token, w.Clinic, w.Doctor, w.Patient, start),
                HttpStatusCode.UnprocessableEntity,
                "error.appointment.off_grid");
        }

        // A start one second off the grid is off the grid too.
        await AssertProblemAsync(
            await BookAsync(_client, w.Desk.Token, w.Clinic, w.Doctor, w.Patient, Cairo(2026, 7, 2, 10, 0).AddSeconds(1)),
            HttpStatusCode.UnprocessableEntity,
            "error.appointment.off_grid");

        // The last slot ends exactly at the period end.
        await BookOkAsync(_client, w, Cairo(2026, 7, 2, 11, 40));
    }

    [Fact]
    public async Task A_grid_with_several_periods_starts_again_at_each_period()
    {
        // Thursday (4): 09:00–10:10 and 10:15–11:00, 20-minute slots.
        var w = await WorldAsync(_fixture, _client, periods: [Period(4, "09:00", "10:10"), Period(4, "10:15", "11:00")]);

        var slots = await SlotStartsAsync(_client, w.Desk.Token, w.Clinic, w.Doctor, "2026-07-02");

        Assert.Equal(
            [Cairo(2026, 7, 2, 9, 0), Cairo(2026, 7, 2, 9, 20), Cairo(2026, 7, 2, 9, 40), Cairo(2026, 7, 2, 10, 15), Cairo(2026, 7, 2, 10, 35)],
            slots);
        await BookOkAsync(_client, w, Cairo(2026, 7, 2, 10, 15));
        await AssertProblemAsync(
            await BookAsync(_client, w.Desk.Token, w.Clinic, w.Doctor, w.Patient, Cairo(2026, 7, 2, 10, 0)),
            HttpStatusCode.UnprocessableEntity,
            "error.appointment.off_grid");

        // Friday has no hours.
        Assert.Empty(await SlotStartsAsync(_client, w.Desk.Token, w.Clinic, w.Doctor, "2026-07-03"));
    }

    [Fact]
    public async Task The_duration_is_the_one_in_effect_on_the_appointments_date()
    {
        var w = await WorldAsync(_fixture, _client);
        var change = await SendAsync(
            _client, HttpMethod.Post, $"/api/doctors/{w.Doctor}/slot-durations", w.Manager.Token, new { slotMinutes = 30, effectiveFrom = "2026-07-10" });
        await AssertStatusAsync(change, HttpStatusCode.OK);

        var before = await BookOkAsync(_client, w, Cairo(2026, 7, 9, 9, 20));
        Assert.Equal(TimeSpan.FromMinutes(20), Length(before));

        var after = await BookOkAsync(_client, w, Cairo(2026, 7, 10, 9, 30));
        Assert.Equal(TimeSpan.FromMinutes(30), Length(after));

        await AssertProblemAsync(
            await BookAsync(_client, w.Desk.Token, w.Clinic, w.Doctor, w.Patient, Cairo(2026, 7, 10, 9, 20)),
            HttpStatusCode.UnprocessableEntity,
            "error.appointment.off_grid");
        Assert.Equal(
            [Cairo(2026, 7, 10, 9, 0), Cairo(2026, 7, 10, 10, 0), Cairo(2026, 7, 10, 10, 30), Cairo(2026, 7, 10, 11, 0), Cairo(2026, 7, 10, 11, 30)],
            await SlotStartsAsync(_client, w.Desk.Token, w.Clinic, w.Doctor, "2026-07-10"));
    }

    [Fact]
    public async Task A_taken_slot_is_409_and_a_cancelled_one_is_free_again()
    {
        var w = await WorldAsync(_fixture, _client);
        var start = Cairo(2026, 7, 2, 10, 0);
        var first = await BookOkAsync(_client, w, start);
        var otherPatient = await NewPatientAsync(_fixture, _client);

        await AssertProblemAsync(
            await BookAsync(_client, w.Desk.Token, w.Clinic, w.Doctor, otherPatient, start),
            HttpStatusCode.Conflict,
            "error.appointment.slot_taken");

        await AssertStatusAsync(await ChangeStatusAsync(_client, w.Desk.Token, first, "cancel"), HttpStatusCode.OK);
        await BookOkAsync(_client, w.Desk.Token, w.Clinic, w.Doctor, otherPatient, start);
    }

    [Fact]
    public async Task A_leave_day_cannot_be_booked_and_has_no_free_slots()
    {
        var w = await WorldAsync(_fixture, _client);
        var leave = await SendAsync(
            _client, HttpMethod.Post, $"/api/doctors/{w.Doctor}/leave-days", w.Manager.Token, new { from = "2026-07-03", to = "2026-07-03" });
        await AssertStatusAsync(leave, HttpStatusCode.OK);

        await AssertProblemAsync(
            await BookAsync(_client, w.Desk.Token, w.Clinic, w.Doctor, w.Patient, Cairo(2026, 7, 3, 10, 0)),
            HttpStatusCode.UnprocessableEntity,
            "error.appointment.on_leave");
        Assert.Empty(await SlotStartsAsync(_client, w.Desk.Token, w.Clinic, w.Doctor, "2026-07-03"));
        Assert.NotEmpty(await SlotStartsAsync(_client, w.Desk.Token, w.Clinic, w.Doctor, "2026-07-04"));
    }

    [Fact]
    public async Task Free_slots_leave_out_past_times_booked_slots_and_days_outside_the_limit()
    {
        var w = await WorldAsync(_fixture, _client);
        await BookOkAsync(_client, w, Cairo(2026, 7, 1, 10, 0));

        // Today: 09:00 has started (it is 09:00), 10:00 is booked.
        var today = await SlotStartsAsync(_client, w.Desk.Token, w.Clinic, w.Doctor, "2026-07-01");
        Assert.Equal(
            [Cairo(2026, 7, 1, 9, 20), Cairo(2026, 7, 1, 9, 40), Cairo(2026, 7, 1, 10, 20), Cairo(2026, 7, 1, 10, 40),
             Cairo(2026, 7, 1, 11, 0), Cairo(2026, 7, 1, 11, 20), Cairo(2026, 7, 1, 11, 40)],
            today);

        var body = await ProblemAsync(await SlotsAsync(_client, w.Desk.Token, w.Clinic, w.Doctor, "2026-07-01"));
        Assert.Equal(20, body.GetProperty("slotMinutes").GetInt32());
        Assert.Equal("2026-07-01", body.GetProperty("date").GetString());

        Assert.Empty(await SlotStartsAsync(_client, w.Desk.Token, w.Clinic, w.Doctor, "2026-06-30"));
        Assert.Empty(await SlotStartsAsync(_client, w.Desk.Token, w.Clinic, w.Doctor, "2026-09-30"));
        Assert.NotEmpty(await SlotStartsAsync(_client, w.Desk.Token, w.Clinic, w.Doctor, "2026-09-29"));

        // An inactive assignment has none.
        await SetAssignmentActiveAsync(_fixture.Factory.Services, w.Doctor, w.Clinic, false);
        Assert.Empty(await SlotStartsAsync(_client, w.Desk.Token, w.Clinic, w.Doctor, "2026-07-02"));
    }

    [Fact]
    public async Task Free_slots_of_an_unknown_or_unassigned_doctor_are_404()
    {
        var w = await WorldAsync(_fixture, _client);
        var stranger = await NewDoctorAsync(_fixture, _client, [await NewClinicAsync(_fixture, _client)]);

        await AssertProblemAsync(
            await SlotsAsync(_client, w.Desk.Token, w.Clinic, 999_999, "2026-07-02"), HttpStatusCode.NotFound, "error.doctor.not_found");
        await AssertProblemAsync(
            await SlotsAsync(_client, w.Desk.Token, w.Clinic, stranger, "2026-07-02"), HttpStatusCode.NotFound, "error.doctor.clinic_not_assigned");
        Assert.Equal(
            "error.appointment.date_required",
            await FieldErrorAsync(await SendAsync(_client, HttpMethod.Get, $"/api/clinics/{w.Clinic}/doctors/{w.Doctor}/available-slots", w.Desk.Token), "date"));
    }

    [Fact]
    public async Task Around_midnight_the_Cairo_date_and_weekday_decide()
    {
        // Thursday (4) 00:00–01:00 and Wednesday (3) 23:00–23:59 only.
        var w = await WorldAsync(_fixture, _client, periods: [Period(4, "00:00", "01:00"), Period(3, "23:00", "23:59")]);

        // 21:00 UTC on Wednesday 1 July is 00:00 on Thursday 2 July in Cairo.
        var thursday = await BookOkAsync(_client, w, new DateTimeOffset(2026, 7, 1, 21, 0, 0, TimeSpan.Zero));
        var wednesday = await BookOkAsync(_client, w.Desk.Token, w.Clinic, w.Doctor, await NewPatientAsync(_fixture, _client), Cairo(2026, 7, 1, 23, 20));

        Assert.Equal([Reference(wednesday)], References(await DayListOkAsync(_client, w.Desk.Token, w.Clinic, "2026-07-01")));
        Assert.Equal([Reference(thursday)], References(await DayListOkAsync(_client, w.Desk.Token, w.Clinic, "2026-07-02")));
        Assert.Equal(
            [new DateTimeOffset(2026, 7, 1, 21, 20, 0, TimeSpan.Zero), new DateTimeOffset(2026, 7, 1, 21, 40, 0, TimeSpan.Zero)],
            await SlotStartsAsync(_client, w.Desk.Token, w.Clinic, w.Doctor, "2026-07-02"));
    }

    [Fact]
    public async Task In_autumn_a_repeated_hour_is_bookable_once_at_its_first_occurrence()
    {
        _fixture.Clock.Now = new DateTimeOffset(2026, 10, 20, 6, 0, 0, TimeSpan.Zero);

        // Thursday 29 October: at 24:00 the clock goes back to 23:00.
        var w = await WorldAsync(_fixture, _client, periods: [Period(4, "22:00", "23:59")]);

        await AssertProblemAsync(
            await BookAsync(_client, w.Desk.Token, w.Clinic, w.Doctor, w.Patient, new DateTimeOffset(2026, 10, 29, 21, 0, 0, TimeSpan.Zero)),
            HttpStatusCode.UnprocessableEntity,
            "error.appointment.off_grid");
        var first = await BookOkAsync(_client, w, new DateTimeOffset(2026, 10, 29, 20, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateTimeOffset(2026, 10, 29, 20, 20, 0, TimeSpan.Zero), first.GetProperty("endUtc").GetDateTimeOffset());
        Assert.Equal(4, (await SlotStartsAsync(_client, w.Desk.Token, w.Clinic, w.Doctor, "2026-10-29")).Length);
        Assert.Equal([Reference(first)], References(await DayListOkAsync(_client, w.Desk.Token, w.Clinic, "2026-10-29")));
    }

    [Fact]
    public async Task In_spring_the_skipped_hour_has_no_slots()
    {
        _fixture.Clock.Now = new DateTimeOffset(2026, 4, 20, 6, 0, 0, TimeSpan.Zero);

        // Friday 24 April: 00:00 jumps to 01:00.
        var w = await WorldAsync(_fixture, _client, periods: [Period(5, "00:00", "02:00")]);

        Assert.Equal(
            [new DateTimeOffset(2026, 4, 23, 22, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 4, 23, 22, 20, 0, TimeSpan.Zero), new DateTimeOffset(2026, 4, 23, 22, 40, 0, TimeSpan.Zero)],
            await SlotStartsAsync(_client, w.Desk.Token, w.Clinic, w.Doctor, "2026-04-24"));
        var booked = await BookOkAsync(_client, w, new DateTimeOffset(2026, 4, 23, 22, 0, 0, TimeSpan.Zero));
        Assert.Equal([Reference(booked)], References(await DayListOkAsync(_client, w.Desk.Token, w.Clinic, "2026-04-24")));
        Assert.Empty((await DayListOkAsync(_client, w.Desk.Token, w.Clinic, "2026-04-23")).EnumerateArray());
    }

    private static TimeSpan Length(JsonElement appointment) =>
        appointment.GetProperty("endUtc").GetDateTimeOffset() - appointment.GetProperty("startUtc").GetDateTimeOffset();
}
