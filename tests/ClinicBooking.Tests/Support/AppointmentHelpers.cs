using System.Net;
using System.Text.Json;
using ClinicBooking.Application.Features.Common;
using ClinicBooking.Domain.Permissions;
using static ClinicBooking.Tests.Support.AuthHelpers;
using static ClinicBooking.Tests.Support.DoctorHelpers;
using static ClinicBooking.Tests.Support.PatientHelpers;
using static ClinicBooking.Tests.Support.SpecialtyHelpers;

namespace ClinicBooking.Tests.Support;

/// <summary>A signed-in user whose user name is kept, so it can sign in again after the clock moves.</summary>
internal sealed record Staff(string Token, long UserId, string UserName);

/// <summary>One clinic, one doctor working there every day, one patient, a desk user and a doctor manager.</summary>
internal sealed record World(long Clinic, long Doctor, long Patient, Staff Desk, Staff Manager);

/// <summary>Helpers for the Appointments tests (D66).</summary>
internal static class AppointmentHelpers
{
    public static readonly string[] AllAppointmentPermissions =
    [
        Permissions.Appointments.Read,
        Permissions.Appointments.Book,
        Permissions.Appointments.Reschedule,
        Permissions.Appointments.Cancel,
        Permissions.Appointments.Record,
    ];

    /// <summary>The UTC instant of a Cairo wall-clock time (the earlier one when it occurs twice).</summary>
    public static DateTimeOffset Cairo(int year, int month, int day, int hour, int minute = 0) =>
        CairoTime.ToUtc(new DateOnly(year, month, day), new TimeOnly(hour, minute))
        ?? throw new ArgumentException("That Cairo time does not exist.");

    /// <summary>A user with the given clinic-scoped permissions in each clinic and the given global ones.</summary>
    public static async Task<Staff> StaffAsync(
        AuthApiFixture fixture,
        HttpClient client,
        IEnumerable<long> clinics,
        IEnumerable<string> clinicPermissions,
        params string[] globalPermissions)
    {
        var user = await CreateUserAsync(fixture.Factory.Services, globalPermissions);
        foreach (var clinic in clinics)
        {
            foreach (var permission in clinicPermissions)
            {
                await GrantAsync(fixture.Factory.Services, user.Id, clinic, permission);
            }
        }

        return await SignInAgainAsync(client, new Staff(string.Empty, user.Id, user.UserName!));
    }

    /// <summary>A new access token for the same user (after the test clock moved past the old one's lifetime).</summary>
    public static async Task<Staff> SignInAgainAsync(HttpClient client, Staff staff)
    {
        var login = await LoginAsync(client, staff.UserName, Password);
        await AssertStatusAsync(login, HttpStatusCode.OK);
        return staff with { Token = await AccessTokenAsync(login) };
    }

    /// <summary>Every weekday from 09:00 to 12:00 (Cairo).</summary>
    public static object[] EveryDay(string start = "09:00", string end = "12:00") =>
        Enumerable.Range(0, 7).Select(day => Period(day, start, end)).ToArray();

    public static async Task<World> WorldAsync(
        AuthApiFixture fixture,
        HttpClient client,
        int slotMinutes = 20,
        object[]? periods = null,
        string? patientName = null,
        string? patientPhone = null)
    {
        var clinic = await NewClinicAsync(fixture, client);
        var doctor = await NewDoctorAsync(fixture, client, [clinic], slotMinutes);
        var manager = await StaffAsync(fixture, client, [clinic], [Permissions.Doctors.Manage]);
        await AssertStatusAsync(await SaveHoursAsync(client, manager.Token, doctor, clinic, periods ?? EveryDay()), HttpStatusCode.OK);

        var patient = await NewPatientAsync(fixture, client, patientName, patientPhone);
        var desk = await StaffAsync(fixture, client, [clinic], AllAppointmentPermissions, Permissions.Patients.Read);
        return new World(clinic, doctor, patient, desk, manager);
    }

    public static async Task<long> NewDoctorAsync(AuthApiFixture fixture, HttpClient client, long[] clinics, int slotMinutes = 20)
    {
        var specialty = await NewSpecialtyAsync(fixture, client);
        var (manager, _) = await DoctorManagerAsync(fixture, client, clinics);
        var doctor = await CreateDoctorOkAsync(client, manager, [specialty], clinics, slotMinutes: slotMinutes);
        return doctor.GetProperty("id").GetInt64();
    }

    public static async Task<long> NewPatientAsync(AuthApiFixture fixture, HttpClient client, string? name = null, string? phone = null)
    {
        var token = await PatientManagerAsync(fixture, client);
        return (await CreatePatientOkAsync(client, token, name, phone)).GetProperty("id").GetInt64();
    }

    public static Task<HttpResponseMessage> BookAsync(
        HttpClient client,
        string token,
        long clinic,
        long doctorId,
        long patientId,
        DateTimeOffset startUtc,
        bool? confirmPatientOverlap = null) =>
        SendAsync(client, HttpMethod.Post, $"/api/clinics/{clinic}/appointments", token, new { doctorId, patientId, startUtc, confirmPatientOverlap });

    public static async Task<JsonElement> BookOkAsync(
        HttpClient client,
        string token,
        long clinic,
        long doctorId,
        long patientId,
        DateTimeOffset startUtc,
        bool? confirmPatientOverlap = null)
    {
        var response = await BookAsync(client, token, clinic, doctorId, patientId, startUtc, confirmPatientOverlap);
        await AssertStatusAsync(response, HttpStatusCode.Created);
        return await ProblemAsync(response);
    }

    public static Task<JsonElement> BookOkAsync(HttpClient client, World world, DateTimeOffset startUtc) =>
        BookOkAsync(client, world.Desk.Token, world.Clinic, world.Doctor, world.Patient, startUtc);

    public static Task<HttpResponseMessage> GetAppointmentAsync(HttpClient client, string token, string reference) =>
        SendAsync(client, HttpMethod.Get, $"/api/appointments/{reference}", token);

    public static async Task<JsonElement> GetAppointmentOkAsync(HttpClient client, string token, string reference)
    {
        var response = await GetAppointmentAsync(client, token, reference);
        await AssertStatusAsync(response, HttpStatusCode.OK);
        return await ProblemAsync(response);
    }

    /// <summary>POST /api/appointments/{reference}/{action} with the row version (cancel, complete, no-show).</summary>
    public static Task<HttpResponseMessage> ChangeStatusAsync(HttpClient client, string token, string reference, string action, string? rowVersion) =>
        SendAsync(client, HttpMethod.Post, $"/api/appointments/{reference}/{action}", token, new { rowVersion });

    public static Task<HttpResponseMessage> ChangeStatusAsync(HttpClient client, string token, JsonElement appointment, string action) =>
        ChangeStatusAsync(client, token, Reference(appointment), action, appointment.GetProperty("rowVersion").GetString());

    public static Task<HttpResponseMessage> RescheduleAsync(
        HttpClient client,
        string token,
        JsonElement appointment,
        DateTimeOffset startUtc,
        bool? confirmPatientOverlap = null,
        string? rowVersion = null) =>
        SendAsync(
            client,
            HttpMethod.Post,
            $"/api/appointments/{Reference(appointment)}/reschedule",
            token,
            new { startUtc, rowVersion = rowVersion ?? appointment.GetProperty("rowVersion").GetString(), confirmPatientOverlap });

    public static Task<HttpResponseMessage> DayListAsync(HttpClient client, string token, long clinic, string date, long? doctorId = null) =>
        SendAsync(client, HttpMethod.Get, $"/api/clinics/{clinic}/appointments?date={date}" + (doctorId is { } d ? $"&doctorId={d}" : ""), token);

    public static async Task<JsonElement> DayListOkAsync(HttpClient client, string token, long clinic, string date, long? doctorId = null)
    {
        var response = await DayListAsync(client, token, clinic, date, doctorId);
        await AssertStatusAsync(response, HttpStatusCode.OK);
        return await ProblemAsync(response);
    }

    public static Task<HttpResponseMessage> SlotsAsync(HttpClient client, string token, long clinic, long doctorId, string date) =>
        SendAsync(client, HttpMethod.Get, $"/api/clinics/{clinic}/doctors/{doctorId}/available-slots?date={date}", token);

    /// <summary>The free slots' starts, as UTC instants.</summary>
    public static async Task<DateTimeOffset[]> SlotStartsAsync(HttpClient client, string token, long clinic, long doctorId, string date)
    {
        var response = await SlotsAsync(client, token, clinic, doctorId, date);
        await AssertStatusAsync(response, HttpStatusCode.OK);
        return (await ProblemAsync(response)).GetProperty("slots").EnumerateArray()
            .Select(s => s.GetProperty("startUtc").GetDateTimeOffset())
            .ToArray();
    }

    public static string Reference(JsonElement appointment) => appointment.GetProperty("bookingReference").GetString()!;

    public static string Status(JsonElement appointment) => appointment.GetProperty("status").GetString()!;

    public static string[] References(JsonElement list) => list.EnumerateArray().Select(Reference).ToArray();

    public static async Task<long> AppointmentCountAsync(HttpResponseMessage response) =>
        (await ProblemAsync(response)).GetProperty("appointmentCount").GetInt64();
}
