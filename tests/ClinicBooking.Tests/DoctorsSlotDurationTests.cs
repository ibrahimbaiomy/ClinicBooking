using System.Net;
using System.Text.Json;
using ClinicBooking.Application.Interfaces;
using ClinicBooking.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using static ClinicBooking.Tests.Support.AuthHelpers;
using static ClinicBooking.Tests.Support.DoctorHelpers;
using static ClinicBooking.Tests.Support.SpecialtyHelpers;

namespace ClinicBooking.Tests;

/// <summary>
/// Slot-duration changes (D43, D61): from a Cairo date after today, at most one pending change, replaced
/// by a new one; needs doctors.manage in any of the doctor's clinics.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class DoctorsSlotDurationTests : IClassFixture<AuthApiFixture>
{
    // 09:00 UTC = 12:00 in Cairo on 1 July 2026.
    private static readonly DateTimeOffset BaseTime = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);

    private const string OtherPermission = "test.other";

    private readonly AuthApiFixture _fixture;
    private readonly HttpClient _client;

    public DoctorsSlotDurationTests(AuthApiFixture fixture)
    {
        _fixture = fixture;
        _fixture.Clock.Now = BaseTime;
        _client = fixture.Factory.CreateClient();
    }

    private sealed record Setup(long Clinic, long DoctorId, string Manager, long ManagerId);

    private async Task<Setup> SetupAsync(int slotMinutes = 15)
    {
        var clinic = await NewClinicAsync(_fixture, _client);
        var specialty = await NewSpecialtyAsync(_fixture, _client);
        var (manager, managerId) = await DoctorManagerAsync(_fixture, _client, clinic);
        var doctor = await CreateDoctorOkAsync(_client, manager, [specialty], [clinic], slotMinutes: slotMinutes);
        return new Setup(clinic, doctor.GetProperty("id").GetInt64(), manager, managerId);
    }

    private Task<HttpResponseMessage> ChangeAsync(string token, long doctorId, int? slotMinutes, string? effectiveFrom) =>
        SendAsync(_client, HttpMethod.Post, $"/api/doctors/{doctorId}/slot-durations", token, new { slotMinutes, effectiveFrom });

    [Fact]
    public async Task A_scheduled_change_shows_as_pending_until_its_date_then_becomes_the_duration()
    {
        var s = await SetupAsync(15);

        var response = await ChangeAsync(s.Manager, s.DoctorId, 30, "2026-07-10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var doctor = await ProblemAsync(response);
        Assert.Equal(15, doctor.GetProperty("slotMinutes").GetInt32());
        Assert.Equal(30, doctor.GetProperty("pendingSlotChange").GetProperty("slotMinutes").GetInt32());
        Assert.Equal("2026-07-10", doctor.GetProperty("pendingSlotChange").GetProperty("effectiveFrom").GetString());

        // 9 July 21:00 UTC is already 10 July (00:00) in Cairo.
        _fixture.Clock.Now = new DateTimeOffset(2026, 7, 9, 21, 0, 0, TimeSpan.Zero);
        var (reader, _) = await SignInAsync(_fixture, _client);
        var later = await GetDoctorAsync(_client, reader, s.DoctorId);
        Assert.Equal(30, later.GetProperty("slotMinutes").GetInt32());
        Assert.Equal(JsonValueKind.Null, later.GetProperty("pendingSlotChange").ValueKind);
    }

    [Fact]
    public async Task A_new_change_replaces_the_pending_one_even_on_the_same_date()
    {
        var s = await SetupAsync();

        Assert.Equal(HttpStatusCode.OK, (await ChangeAsync(s.Manager, s.DoctorId, 30, "2026-07-10")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ChangeAsync(s.Manager, s.DoctorId, 45, "2026-07-10")).StatusCode);
        var last = await ProblemAsync(await ChangeAsync(s.Manager, s.DoctorId, 20, "2026-08-01"));

        Assert.Equal(20, last.GetProperty("pendingSlotChange").GetProperty("slotMinutes").GetInt32());
        Assert.Equal("2026-08-01", last.GetProperty("pendingSlotChange").GetProperty("effectiveFrom").GetString());
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var rows = await db.DoctorSlotDurations.AsNoTracking().Where(d => d.DoctorId == s.DoctorId).OrderBy(d => d.EffectiveFrom).ToListAsync();
        Assert.Equal([(15, new DateOnly(2026, 7, 1)), (20, new DateOnly(2026, 8, 1))], rows.Select(r => (r.SlotMinutes, r.EffectiveFrom)).ToArray());
        Assert.Equal(s.ManagerId, rows[1].CreatedBy);
    }

    [Theory]
    [InlineData("2026-07-01")] // today
    [InlineData("2026-06-30")] // past
    public async Task The_date_must_be_after_today_in_Cairo(string effectiveFrom)
    {
        var s = await SetupAsync();

        var response = await ChangeAsync(s.Manager, s.DoctorId, 30, effectiveFrom);

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "error.doctor.effective_from_not_future");
    }

    [Fact]
    public async Task Today_is_the_Cairo_date_not_the_UTC_one()
    {
        // 22:30 UTC on 1 July is 01:30 on 2 July in Cairo.
        _fixture.Clock.Now = new DateTimeOffset(2026, 7, 1, 22, 30, 0, TimeSpan.Zero);
        var s = await SetupAsync();

        await AssertProblemAsync(await ChangeAsync(s.Manager, s.DoctorId, 30, "2026-07-02"), HttpStatusCode.UnprocessableEntity, "error.doctor.effective_from_not_future");
        Assert.Equal(HttpStatusCode.OK, (await ChangeAsync(s.Manager, s.DoctorId, 30, "2026-07-03")).StatusCode);
    }

    [Theory]
    [InlineData(null, "2026-07-10", "slotMinutes", "error.doctor.slot_minutes_required")]
    [InlineData(17, "2026-07-10", "slotMinutes", "error.doctor.slot_minutes_invalid")]
    [InlineData(130, "2026-07-10", "slotMinutes", "error.doctor.slot_minutes_invalid")]
    [InlineData(30, null, "effectiveFrom", "error.doctor.effective_from_required")]
    public async Task Invalid_input_is_a_400_on_its_field(int? minutes, string? effectiveFrom, string field, string key)
    {
        var s = await SetupAsync();

        Assert.Equal(key, await FieldErrorAsync(await ChangeAsync(s.Manager, s.DoctorId, minutes, effectiveFrom), field));
    }

    [Fact]
    public async Task Changing_needs_doctors_manage_in_any_of_the_doctors_clinics()
    {
        var s = await SetupAsync();
        var (nobody, _) = await SignInAsync(_fixture, _client);
        var (other, otherId) = await SignInAsync(_fixture, _client);
        await GrantAsync(_fixture.Factory.Services, otherId, s.Clinic, OtherPermission);
        var (secondManager, _) = await DoctorManagerAsync(_fixture, _client, s.Clinic);

        await AssertProblemAsync(await ChangeAsync(nobody, s.DoctorId, 30, "2026-07-10"), HttpStatusCode.NotFound, "error.doctor.not_found");
        await AssertProblemAsync(await ChangeAsync(other, s.DoctorId, 30, "2026-07-10"), HttpStatusCode.Forbidden, "error.auth.forbidden");
        await AssertProblemAsync(await ChangeAsync(secondManager, 987654321, 30, "2026-07-10"), HttpStatusCode.NotFound, "error.doctor.not_found");
        await AssertProblemAsync(
            await _client.SendAsync(Json(HttpMethod.Post, $"/api/doctors/{s.DoctorId}/slot-durations", null, new { slotMinutes = 30, effectiveFrom = "2026-07-10" })),
            HttpStatusCode.Unauthorized,
            "error.auth.unauthorized");
        Assert.Equal(HttpStatusCode.OK, (await ChangeAsync(secondManager, s.DoctorId, 30, "2026-07-10")).StatusCode);
    }

    [Fact]
    public async Task Working_hours_are_checked_against_todays_duration_and_are_not_revalidated_by_a_change()
    {
        var s = await SetupAsync(15);
        Assert.Equal(HttpStatusCode.OK, (await SaveHoursAsync(_client, s.Manager, s.DoctorId, s.Clinic, Period(1, "09:00", "09:20"))).StatusCode);

        // A longer duration later does not touch the stored 20-minute period ...
        Assert.Equal(HttpStatusCode.OK, (await ChangeAsync(s.Manager, s.DoctorId, 60, "2026-07-10")).StatusCode);
        Assert.Equal(["1 09:00:00-09:20:00"], PeriodTexts(await GetHoursOkAsync(_client, s.Manager, s.DoctorId, s.Clinic)));

        // ... and a save today still measures against today's 15 minutes.
        Assert.Equal(HttpStatusCode.OK, (await SaveHoursAsync(_client, s.Manager, s.DoctorId, s.Clinic, Period(1, "09:00", "09:15"))).StatusCode);
        await AssertProblemAsync(
            await SaveHoursAsync(_client, s.Manager, s.DoctorId, s.Clinic, Period(1, "09:00", "09:10")),
            HttpStatusCode.UnprocessableEntity,
            "error.doctor.period_shorter_than_slot");
    }
}
