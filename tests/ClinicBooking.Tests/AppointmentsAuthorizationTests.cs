using System.Net;
using System.Text.Json;
using ClinicBooking.Domain.Permissions;
using ClinicBooking.Tests.Support;
using static ClinicBooking.Tests.Support.AppointmentHelpers;
using static ClinicBooking.Tests.Support.AuthHelpers;
using static ClinicBooking.Tests.Support.DoctorHelpers;
using static ClinicBooking.Tests.Support.SpecialtyHelpers;

namespace ClinicBooking.Tests;

/// <summary>
/// The five clinic-scoped appointment permissions, each opening only its own endpoints (D66), and the 404/403 rule
/// for an appointment addressed by its reference (D6, D57).
/// </summary>
[Collection(SqlServerCollection.Name)]
public class AppointmentsAuthorizationTests : IClassFixture<AuthApiFixture>
{
    private static readonly DateTimeOffset BaseTime = new(2026, 7, 1, 6, 0, 0, TimeSpan.Zero);

    private readonly AuthApiFixture _fixture;
    private readonly HttpClient _client;

    public AppointmentsAuthorizationTests(AuthApiFixture fixture)
    {
        _fixture = fixture;
        _fixture.Clock.Now = BaseTime;
        _client = fixture.Factory.CreateClient();
    }

    private sealed record Calls(
        HttpStatusCode Book,
        HttpStatusCode DayList,
        HttpStatusCode Slots,
        HttpStatusCode Get,
        HttpStatusCode Reschedule,
        HttpStatusCode Cancel,
        HttpStatusCode Complete);

    /// <summary>Every endpoint, called by a user holding only <paramref name="permission"/> in the clinic.</summary>
    private async Task<Calls> CallEverythingAsync(string? permission, bool inOtherClinic = false)
    {
        var w = await WorldAsync(_fixture, _client);
        var future = await BookOkAsync(_client, w, Cairo(2026, 7, 2, 10, 0));
        var started = await BookOkAsync(_client, w.Desk.Token, w.Clinic, w.Doctor, await NewPatientAsync(_fixture, _client), Cairo(2026, 7, 1, 9, 20));

        var clinic = inOtherClinic ? await NewClinicAsync(_fixture, _client) : w.Clinic;
        var user = await StaffAsync(_fixture, _client, [clinic], permission is null ? [] : [permission]);

        _fixture.Clock.Now = Cairo(2026, 7, 1, 9, 30);
        user = await SignInAgainAsync(_client, user);

        var book = await BookAsync(_client, user.Token, w.Clinic, w.Doctor, w.Patient, Cairo(2026, 7, 3, 10, 0));
        var list = await DayListAsync(_client, user.Token, w.Clinic, "2026-07-02");
        var slots = await SlotsAsync(_client, user.Token, w.Clinic, w.Doctor, "2026-07-02");
        var get = await GetAppointmentAsync(_client, user.Token, Reference(future));
        var reschedule = await RescheduleAsync(_client, user.Token, future, Cairo(2026, 7, 2, 11, 0));
        var cancel = await ChangeStatusAsync(_client, user.Token, Reference(future), "cancel", await RowVersionAsync(w, future));
        var complete = await ChangeStatusAsync(_client, user.Token, started, "complete");

        foreach (var response in new[] { get, reschedule, cancel, complete }.Where(r => r.StatusCode == HttpStatusCode.NotFound))
        {
            await AssertProblemAsync(response, HttpStatusCode.NotFound, "error.appointment.not_found");
        }

        foreach (var response in new[] { book, list, slots, get, reschedule, cancel, complete }.Where(r => r.StatusCode == HttpStatusCode.Forbidden))
        {
            await AssertProblemAsync(response, HttpStatusCode.Forbidden, "error.auth.forbidden");
        }

        return new Calls(book.StatusCode, list.StatusCode, slots.StatusCode, get.StatusCode, reschedule.StatusCode, cancel.StatusCode, complete.StatusCode);
    }

    // The current row version (a reschedule may have moved it).
    private async Task<string> RowVersionAsync(World w, JsonElement appointment)
    {
        var desk = await SignInAgainAsync(_client, w.Desk);
        return (await GetAppointmentOkAsync(_client, desk.Token, Reference(appointment))).GetProperty("rowVersion").GetString()!;
    }

    private const HttpStatusCode Ok = HttpStatusCode.OK;
    private const HttpStatusCode Created = HttpStatusCode.Created;
    private const HttpStatusCode Forbidden = HttpStatusCode.Forbidden;
    private const HttpStatusCode NotFound = HttpStatusCode.NotFound;

    [Fact]
    public async Task Read_opens_the_day_list_and_one_appointment_only() =>
        Assert.Equal(new Calls(Forbidden, Ok, Forbidden, Ok, Forbidden, Forbidden, Forbidden), await CallEverythingAsync(Permissions.Appointments.Read));

    [Fact]
    public async Task Book_opens_booking_and_the_free_slots_only() =>
        Assert.Equal(new Calls(Created, Forbidden, Ok, Forbidden, Forbidden, Forbidden, Forbidden), await CallEverythingAsync(Permissions.Appointments.Book));

    [Fact]
    public async Task Reschedule_opens_rescheduling_and_the_free_slots_only() =>
        Assert.Equal(new Calls(Forbidden, Forbidden, Ok, Forbidden, Ok, Forbidden, Forbidden), await CallEverythingAsync(Permissions.Appointments.Reschedule));

    [Fact]
    public async Task Cancel_opens_cancelling_only() =>
        Assert.Equal(new Calls(Forbidden, Forbidden, Forbidden, Forbidden, Forbidden, Ok, Forbidden), await CallEverythingAsync(Permissions.Appointments.Cancel));

    [Fact]
    public async Task Record_opens_recording_only() =>
        Assert.Equal(new Calls(Forbidden, Forbidden, Forbidden, Forbidden, Forbidden, Forbidden, Ok), await CallEverythingAsync(Permissions.Appointments.Record));

    [Fact]
    public async Task Another_clinic_scoped_permission_in_the_clinic_is_403_by_reference() =>
        Assert.Equal(new Calls(Forbidden, Forbidden, Forbidden, Forbidden, Forbidden, Forbidden, Forbidden), await CallEverythingAsync(Permissions.Doctors.Manage));

    [Fact]
    public async Task Permissions_held_only_in_another_clinic_make_the_appointment_not_found() =>
        Assert.Equal(new Calls(Forbidden, Forbidden, Forbidden, NotFound, NotFound, NotFound, NotFound), await CallEverythingAsync(Permissions.Appointments.Read, inOtherClinic: true));

    [Fact]
    public async Task No_permission_at_all_is_403_in_the_route_and_404_by_reference() =>
        Assert.Equal(new Calls(Forbidden, Forbidden, Forbidden, NotFound, NotFound, NotFound, NotFound), await CallEverythingAsync(null));

    [Fact]
    public async Task Global_permissions_never_satisfy_appointment_permissions()
    {
        var w = await WorldAsync(_fixture, _client);
        var booked = await BookOkAsync(_client, w, Cairo(2026, 7, 2, 10, 0));
        var admin = await StaffAsync(_fixture, _client, [], [], [.. Permissions.Global]);

        Assert.Equal(HttpStatusCode.Forbidden, (await DayListAsync(_client, admin.Token, w.Clinic, "2026-07-02")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await GetAppointmentAsync(_client, admin.Token, Reference(booked))).StatusCode);
    }

    [Fact]
    public async Task A_soft_deleted_clinic_grants_nothing()
    {
        var w = await WorldAsync(_fixture, _client);
        var booked = await BookOkAsync(_client, w, Cairo(2026, 7, 2, 10, 0));

        await SoftDeleteClinicAsync(_fixture.Factory.Services, w.Clinic);

        Assert.Equal(HttpStatusCode.Forbidden, (await DayListAsync(_client, w.Desk.Token, w.Clinic, "2026-07-02")).StatusCode);
        await AssertProblemAsync(await GetAppointmentAsync(_client, w.Desk.Token, Reference(booked)), HttpStatusCode.NotFound, "error.appointment.not_found");
    }

    [Fact]
    public async Task Anonymous_callers_get_401()
    {
        var w = await WorldAsync(_fixture, _client);

        Assert.Equal(HttpStatusCode.Unauthorized, (await DayListAsync(_client, null!, w.Clinic, "2026-07-02")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await GetAppointmentAsync(_client, null!, "ABCDEF")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SlotsAsync(_client, null!, w.Clinic, w.Doctor, "2026-07-02")).StatusCode);
    }
}
