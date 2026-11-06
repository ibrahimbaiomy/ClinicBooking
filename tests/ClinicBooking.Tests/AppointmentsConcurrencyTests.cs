using System.Net;
using ClinicBooking.Domain.Entities;
using ClinicBooking.Domain.Enums;
using ClinicBooking.Domain.Permissions;
using ClinicBooking.Domain.ValueObjects;
using ClinicBooking.Infrastructure.Persistence;
using ClinicBooking.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using static ClinicBooking.Tests.Support.AppointmentHelpers;
using static ClinicBooking.Tests.Support.AuthHelpers;
using static ClinicBooking.Tests.Support.DoctorHelpers;

namespace ClinicBooking.Tests;

/// <summary>
/// Double-booking protection (D31, D43): parallel bookings of one doctor's slot produce at most one appointment,
/// also when they come from two clinics. The unique index (doctor, start) is the guard; the losers get a 409.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class AppointmentsConcurrencyTests : IClassFixture<AuthApiFixture>
{
    private static readonly DateTimeOffset BaseTime = new(2026, 7, 1, 6, 0, 0, TimeSpan.Zero);

    private static readonly string[] LosingKeys = ["error.appointment.slot_taken", "error.concurrency.conflict"];

    private readonly AuthApiFixture _fixture;
    private readonly HttpClient _client;

    public AppointmentsConcurrencyTests(AuthApiFixture fixture)
    {
        _fixture = fixture;
        _fixture.Clock.Now = BaseTime;
        _client = fixture.Factory.CreateClient();
    }

    [Fact]
    public async Task Parallel_bookings_of_the_same_slot_produce_at_most_one_appointment()
    {
        var w = await WorldAsync(_fixture, _client);
        var start = Cairo(2026, 7, 2, 10, 0);
        var patients = new List<long>();
        for (var i = 0; i < 8; i++)
        {
            patients.Add(await NewPatientAsync(_fixture, _client));
        }

        var responses = await Task.WhenAll(patients.Select(p => BookAsync(_client, w.Desk.Token, w.Clinic, w.Doctor, p, start)));

        await AssertOneWinnerAsync(responses);
        Assert.Equal(1, await LiveCountAsync(w.Doctor, start));
    }

    [Fact]
    public async Task Parallel_bookings_of_one_doctor_in_two_clinics_at_the_same_time_produce_at_most_one()
    {
        var clinicA = await NewClinicAsync(_fixture, _client);
        var clinicB = await NewClinicAsync(_fixture, _client);
        var doctor = await NewDoctorAsync(_fixture, _client, [clinicA, clinicB]);
        var manager = await StaffAsync(_fixture, _client, [clinicA], [Permissions.Doctors.Manage]);
        await AssertStatusAsync(await SaveHoursAsync(_client, manager.Token, doctor, clinicA, EveryDay()), HttpStatusCode.OK);

        // The API refuses overlapping hours in two clinics (D32); written directly here so that only the
        // database guard stands between the two bookings.
        await AddHoursDirectlyAsync(doctor, clinicB, WeeklyPeriod.Create(4, new TimeOnly(9, 0), new TimeOnly(12, 0)));

        var deskA = await StaffAsync(_fixture, _client, [clinicA], [Permissions.Appointments.Book]);
        var deskB = await StaffAsync(_fixture, _client, [clinicB], [Permissions.Appointments.Book]);
        var start = Cairo(2026, 7, 2, 10, 0);
        var patients = new List<long>();
        for (var i = 0; i < 6; i++)
        {
            patients.Add(await NewPatientAsync(_fixture, _client));
        }

        var responses = await Task.WhenAll(patients.Select((p, i) => i % 2 == 0
            ? BookAsync(_client, deskA.Token, clinicA, doctor, p, start)
            : BookAsync(_client, deskB.Token, clinicB, doctor, p, start)));

        await AssertOneWinnerAsync(responses);
        Assert.Equal(1, await LiveCountAsync(doctor, start));

        // Taken in either clinic, the time is not free in the other one (any clinic counts).
        Assert.DoesNotContain(start, await SlotStartsAsync(_client, deskA.Token, clinicA, doctor, "2026-07-02"));
        Assert.DoesNotContain(start, await SlotStartsAsync(_client, deskB.Token, clinicB, doctor, "2026-07-02"));
        await AssertProblemAsync(
            await BookAsync(_client, deskA.Token, clinicA, doctor, await NewPatientAsync(_fixture, _client), start),
            HttpStatusCode.Conflict,
            "error.appointment.slot_taken");
    }

    private static async Task AssertOneWinnerAsync(HttpResponseMessage[] responses)
    {
        Assert.True(responses.Count(r => r.StatusCode == HttpStatusCode.Created) <= 1);
        foreach (var loser in responses.Where(r => r.StatusCode != HttpStatusCode.Created))
        {
            await AssertStatusAsync(loser, HttpStatusCode.Conflict);
            Assert.Contains((await ProblemAsync(loser)).GetProperty("title").GetString(), LosingKeys);
        }
    }

    private async Task<int> LiveCountAsync(long doctorId, DateTimeOffset start)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Appointments.CountAsync(a => a.DoctorId == doctorId && a.StartUtc == start && a.Status != AppointmentStatus.Cancelled);
    }

    private async Task AddHoursDirectlyAsync(long doctorId, long clinicId, WeeklyPeriod period)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var assignment = await db.DoctorClinics.SingleAsync(c => c.DoctorId == doctorId && c.ClinicId == clinicId);
        assignment.WorkingHours.Add(WorkingHourPeriod.Create(period));
        await db.SaveChangesAsync();
    }
}
