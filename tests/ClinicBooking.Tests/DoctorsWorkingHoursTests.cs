using System.Net;
using ClinicBooking.Application.Interfaces;
using ClinicBooking.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using static ClinicBooking.Tests.Support.AuthHelpers;
using static ClinicBooking.Tests.Support.DoctorHelpers;
using static ClinicBooking.Tests.Support.SpecialtyHelpers;

namespace ClinicBooking.Tests;

/// <summary>
/// Weekly working hours per (doctor, clinic) (D12, D32, D61): full replace, Cairo times, overlap within
/// the clinic and across the doctor's other active clinics, periods at least one slot long.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class DoctorsWorkingHoursTests : IClassFixture<AuthApiFixture>
{
    private const string OtherPermission = "test.other";

    private readonly AuthApiFixture _fixture;
    private readonly HttpClient _client;

    public DoctorsWorkingHoursTests(AuthApiFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Factory.CreateClient();
    }

    private sealed record Setup(long A, long B, long DoctorId, string Manager);

    /// <summary>A doctor in clinics A and B (both active), 30-minute slots, and a manager of both.</summary>
    private async Task<Setup> SetupAsync(int slotMinutes = 30)
    {
        var a = await NewClinicAsync(_fixture, _client);
        var b = await NewClinicAsync(_fixture, _client);
        var specialty = await NewSpecialtyAsync(_fixture, _client);
        var (manager, _) = await DoctorManagerAsync(_fixture, _client, a, b);
        var doctor = await CreateDoctorOkAsync(_client, manager, [specialty], [a, b], slotMinutes: slotMinutes);
        return new Setup(a, b, doctor.GetProperty("id").GetInt64(), manager);
    }

    // ---- reading ----------------------------------------------------------------------------

    [Fact]
    public async Task Any_signed_in_user_reads_an_empty_week_of_a_new_assignment()
    {
        var s = await SetupAsync();
        var (nobody, _) = await SignInAsync(_fixture, _client);

        var hours = await GetHoursOkAsync(_client, nobody, s.DoctorId, s.A);

        Assert.Equal(s.DoctorId, hours.GetProperty("doctorId").GetInt64());
        Assert.Equal(s.A, hours.GetProperty("clinicId").GetInt64());
        Assert.True(hours.GetProperty("isActive").GetBoolean());
        Assert.Empty(hours.GetProperty("periods").EnumerateArray());
        Assert.Equal(12, hours.GetProperty("rowVersion").GetString()!.Length);
    }

    [Fact]
    public async Task Reading_an_unknown_doctor_clinic_or_assignment_is_404_with_its_own_key()
    {
        var s = await SetupAsync();
        var unassigned = await NewClinicAsync(_fixture, _client);
        var deleted = await NewClinicAsync(_fixture, _client);
        await SoftDeleteClinicAsync(_fixture.Factory.Services, deleted);
        var (nobody, _) = await SignInAsync(_fixture, _client);

        await AssertProblemAsync(await GetHoursAsync(_client, nobody, 987654321, s.A), HttpStatusCode.NotFound, "error.doctor.not_found");
        await AssertProblemAsync(await GetHoursAsync(_client, nobody, s.DoctorId, 987654321), HttpStatusCode.NotFound, "error.clinic.not_found");
        await AssertProblemAsync(await GetHoursAsync(_client, nobody, s.DoctorId, deleted), HttpStatusCode.NotFound, "error.clinic.not_found");
        await AssertProblemAsync(await GetHoursAsync(_client, nobody, s.DoctorId, unassigned), HttpStatusCode.NotFound, "error.doctor.clinic_not_assigned");
        await AssertProblemAsync(await _client.GetAsync(HoursUrl(s.DoctorId, s.A)), HttpStatusCode.Unauthorized, "error.auth.unauthorized");
    }

    // ---- saving -----------------------------------------------------------------------------

    [Fact]
    public async Task Saving_replaces_the_whole_week_and_returns_it_ordered()
    {
        var s = await SetupAsync();

        var first = await SaveHoursAsync(_client, s.Manager, s.DoctorId, s.A,
            Period(1, "17:00", "21:00"), Period(1, "09:00", "13:00"), Period(0, "10:00", "12:00"));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(["0 10:00:00-12:00:00", "1 09:00:00-13:00:00", "1 17:00:00-21:00:00"], PeriodTexts(await ProblemAsync(first)));

        var second = await SaveHoursAsync(_client, s.Manager, s.DoctorId, s.A, Period(6, "08:30", "10:00"));
        Assert.Equal(["6 08:30:00-10:00:00"], PeriodTexts(await ProblemAsync(second)));
        Assert.Equal(["6 08:30:00-10:00:00"], PeriodTexts(await GetHoursOkAsync(_client, s.Manager, s.DoctorId, s.A)));

        var cleared = await SaveHoursAsync(_client, s.Manager, s.DoctorId, s.A);
        Assert.Empty(PeriodTexts(await ProblemAsync(cleared)));

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        Assert.Equal(0, await db.WorkingHourPeriods.CountAsync(p => db.DoctorClinics.Any(c => c.Id == p.DoctorClinicId && c.DoctorId == s.DoctorId)));
    }

    [Fact]
    public async Task Times_with_seconds_written_out_are_accepted_too()
    {
        var s = await SetupAsync();

        var response = await SaveHoursAsync(_client, s.Manager, s.DoctorId, s.A, Period(2, "09:00:00", "10:00:00"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_assignments_row_version_moves_and_a_stale_one_is_409()
    {
        var s = await SetupAsync();
        var original = (await GetHoursOkAsync(_client, s.Manager, s.DoctorId, s.A)).GetProperty("rowVersion").GetString();

        var first = await PutHoursAsync(_client, s.Manager, s.DoctorId, s.A, [Period(0, "09:00", "10:00")], original);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.NotEqual(original, (await ProblemAsync(first)).GetProperty("rowVersion").GetString());

        var stale = await PutHoursAsync(_client, s.Manager, s.DoctorId, s.A, [Period(0, "11:00", "12:00")], original);
        await AssertProblemAsync(stale, HttpStatusCode.Conflict, "error.concurrency.conflict");
        Assert.Equal(["0 09:00:00-10:00:00"], PeriodTexts(await GetHoursOkAsync(_client, s.Manager, s.DoctorId, s.A)));
    }

    [Fact]
    public async Task The_audit_fields_of_the_periods_name_the_real_user()
    {
        var s = await SetupAsync();
        var (editor, editorId) = await DoctorManagerAsync(_fixture, _client, s.A);

        Assert.Equal(HttpStatusCode.OK, (await SaveHoursAsync(_client, editor, s.DoctorId, s.A, Period(3, "09:00", "10:00"))).StatusCode);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var assignment = await db.DoctorClinics.AsNoTracking().SingleAsync(c => c.DoctorId == s.DoctorId && c.ClinicId == s.A);
        var period = await db.WorkingHourPeriods.AsNoTracking().SingleAsync(p => p.DoctorClinicId == assignment.Id);
        Assert.Equal(editorId, period.CreatedBy);
        Assert.Equal(editorId, assignment.UpdatedBy);
    }

    // ---- who may save -----------------------------------------------------------------------

    [Fact]
    public async Task Saving_needs_doctors_manage_in_that_clinic_not_another_of_the_doctors_clinics()
    {
        var s = await SetupAsync();
        var (managerB, _) = await DoctorManagerAsync(_fixture, _client, s.B);
        var (nobody, _) = await SignInAsync(_fixture, _client);
        var rowVersion = (await GetHoursOkAsync(_client, nobody, s.DoctorId, s.A)).GetProperty("rowVersion").GetString();

        await AssertProblemAsync(await PutHoursAsync(_client, managerB, s.DoctorId, s.A, [], rowVersion), HttpStatusCode.Forbidden, "error.auth.forbidden");
        await AssertProblemAsync(await PutHoursAsync(_client, nobody, s.DoctorId, s.A, [], rowVersion), HttpStatusCode.Forbidden, "error.auth.forbidden");
        Assert.Equal(HttpStatusCode.OK, (await PutHoursAsync(_client, managerB, s.DoctorId, s.B, [], (await GetHoursOkAsync(_client, nobody, s.DoctorId, s.B)).GetProperty("rowVersion").GetString())).StatusCode);
    }

    [Fact]
    public async Task Saving_in_a_deleted_or_unknown_clinic_is_403_from_the_policy()
    {
        var s = await SetupAsync();
        var doomed = await NewClinicAsync(_fixture, _client);
        var (manager, _) = await DoctorManagerAsync(_fixture, _client, doomed);
        await SoftDeleteClinicAsync(_fixture.Factory.Services, doomed);

        await AssertProblemAsync(await PutHoursAsync(_client, manager, s.DoctorId, doomed, [], "AAAAAAAAAAA="), HttpStatusCode.Forbidden, "error.auth.forbidden");
        await AssertProblemAsync(await PutHoursAsync(_client, manager, s.DoctorId, 987654321, [], "AAAAAAAAAAA="), HttpStatusCode.Forbidden, "error.auth.forbidden");
    }

    [Fact]
    public async Task After_the_policy_an_unknown_doctor_or_an_unassigned_clinic_is_404()
    {
        var s = await SetupAsync();
        var other = await NewClinicAsync(_fixture, _client);
        var (manager, _) = await DoctorManagerAsync(_fixture, _client, other);

        await AssertProblemAsync(await PutHoursAsync(_client, manager, 987654321, other, [], "AAAAAAAAAAA="), HttpStatusCode.NotFound, "error.doctor.not_found");
        await AssertProblemAsync(await PutHoursAsync(_client, manager, s.DoctorId, other, [], "AAAAAAAAAAA="), HttpStatusCode.NotFound, "error.doctor.clinic_not_assigned");
    }

    [Fact]
    public async Task Another_clinic_permission_alone_is_403()
    {
        var s = await SetupAsync();
        var (token, userId) = await SignInAsync(_fixture, _client);
        await GrantAsync(_fixture.Factory.Services, userId, s.A, OtherPermission);

        await AssertProblemAsync(await PutHoursAsync(_client, token, s.DoctorId, s.A, [], "AAAAAAAAAAA="), HttpStatusCode.Forbidden, "error.auth.forbidden");
    }

    // ---- validation (400) -------------------------------------------------------------------

    public static TheoryData<object, string, string> InvalidPeriods => new()
    {
        { new { dayOfWeek = 7, start = "09:00", end = "10:00" }, "periods[0].dayOfWeek", "error.doctor.period_day_invalid" },
        { new { dayOfWeek = -1, start = "09:00", end = "10:00" }, "periods[0].dayOfWeek", "error.doctor.period_day_invalid" },
        { new { start = "09:00", end = "10:00" }, "periods[0].dayOfWeek", "error.doctor.period_day_invalid" },
        { new { dayOfWeek = 1, end = "10:00" }, "periods[0].start", "error.doctor.period_start_required" },
        { new { dayOfWeek = 1, start = "09:00" }, "periods[0].end", "error.doctor.period_end_required" },
        { new { dayOfWeek = 1, start = "09:00:30", end = "10:00" }, "periods[0].start", "error.doctor.period_time_invalid" },
        { new { dayOfWeek = 1, start = "10:00", end = "09:00" }, "periods[0].end", "error.doctor.period_end_not_after_start" },
        { new { dayOfWeek = 1, start = "10:00", end = "10:00" }, "periods[0].end", "error.doctor.period_end_not_after_start" },
        { new { dayOfWeek = 1, start = "22:00", end = "00:00" }, "periods[0].end", "error.doctor.period_end_not_after_start" }
    };

    [Theory]
    [MemberData(nameof(InvalidPeriods))]
    public async Task An_invalid_period_is_a_400_on_its_field(object period, string field, string key)
    {
        var s = await SetupAsync();
        var rowVersion = (await GetHoursOkAsync(_client, s.Manager, s.DoctorId, s.A)).GetProperty("rowVersion").GetString();

        var response = await PutHoursAsync(_client, s.Manager, s.DoctorId, s.A, [period], rowVersion);

        Assert.Equal(key, await FieldErrorAsync(response, field));
    }

    [Fact]
    public async Task The_week_is_required_and_holds_at_most_50_periods()
    {
        var s = await SetupAsync();
        var rowVersion = (await GetHoursOkAsync(_client, s.Manager, s.DoctorId, s.A)).GetProperty("rowVersion").GetString();
        var tooMany = Enumerable.Range(0, 51).Select(i => Period(i % 7, $"{i / 7 + 1:00}:00", $"{i / 7 + 1:00}:30")).ToArray();

        Assert.Equal("error.doctor.periods_required", await FieldErrorAsync(await PutHoursAsync(_client, s.Manager, s.DoctorId, s.A, null, rowVersion), "periods"));
        Assert.Equal("error.doctor.periods_too_many", await FieldErrorAsync(await PutHoursAsync(_client, s.Manager, s.DoctorId, s.A, tooMany, rowVersion), "periods"));
        Assert.Equal("error.concurrency.row_version_required", await FieldErrorAsync(await PutHoursAsync(_client, s.Manager, s.DoctorId, s.A, [], null), "rowVersion"));
    }

    [Fact]
    public async Task A_time_that_is_not_a_time_is_a_400()
    {
        var s = await SetupAsync();

        var response = await PutHoursAsync(_client, s.Manager, s.DoctorId, s.A, [Period(1, "25:00", "26:00")], "AAAAAAAAAAA=");

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "error.validation.failed");
    }

    // ---- business rules (422) ---------------------------------------------------------------

    [Fact]
    public async Task Periods_overlapping_on_the_same_day_are_422_but_touching_ones_and_other_days_are_fine()
    {
        var s = await SetupAsync();

        var overlap = await SaveHoursAsync(_client, s.Manager, s.DoctorId, s.A, Period(1, "09:00", "13:00"), Period(1, "12:30", "15:00"));
        await AssertProblemAsync(overlap, HttpStatusCode.UnprocessableEntity, "error.doctor.periods_overlap");

        var touching = await SaveHoursAsync(_client, s.Manager, s.DoctorId, s.A,
            Period(1, "09:00", "13:00"), Period(1, "13:00", "15:00"), Period(2, "09:00", "13:00"));
        Assert.Equal(HttpStatusCode.OK, touching.StatusCode);
    }

    [Fact]
    public async Task A_period_shorter_than_todays_slot_is_422_and_one_slot_exactly_is_fine()
    {
        var s = await SetupAsync(slotMinutes: 30);

        var tooShort = await SaveHoursAsync(_client, s.Manager, s.DoctorId, s.A, Period(1, "09:00", "09:25"));
        await AssertProblemAsync(tooShort, HttpStatusCode.UnprocessableEntity, "error.doctor.period_shorter_than_slot");

        var exact = await SaveHoursAsync(_client, s.Manager, s.DoctorId, s.A, Period(1, "09:00", "09:30"));
        Assert.Equal(HttpStatusCode.OK, exact.StatusCode);
    }

    [Fact]
    public async Task A_period_overlapping_another_active_clinic_on_the_same_day_is_422()
    {
        var s = await SetupAsync();
        Assert.Equal(HttpStatusCode.OK, (await SaveHoursAsync(_client, s.Manager, s.DoctorId, s.A, Period(0, "09:00", "13:00"))).StatusCode);

        var clash = await SaveHoursAsync(_client, s.Manager, s.DoctorId, s.B, Period(0, "12:00", "14:00"));
        await AssertProblemAsync(clash, HttpStatusCode.UnprocessableEntity, "error.doctor.period_overlaps_other_clinic");

        var after = await SaveHoursAsync(_client, s.Manager, s.DoctorId, s.B, Period(0, "13:00", "15:00"), Period(1, "09:00", "13:00"));
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    [Fact]
    public async Task Another_doctors_hours_never_conflict()
    {
        var s = await SetupAsync();
        var specialty = await NewSpecialtyAsync(_fixture, _client);
        var other = await CreateDoctorOkAsync(_client, s.Manager, [specialty], [s.A]);
        Assert.Equal(HttpStatusCode.OK, (await SaveHoursAsync(_client, s.Manager, s.DoctorId, s.A, Period(0, "09:00", "13:00"))).StatusCode);

        var response = await SaveHoursAsync(_client, s.Manager, other.GetProperty("id").GetInt64(), s.A, Period(0, "09:00", "13:00"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_inactive_assignment_may_be_saved_and_is_ignored_by_the_cross_clinic_check()
    {
        var s = await SetupAsync();
        await SetAssignmentActiveAsync(_fixture.Factory.Services, s.DoctorId, s.B, false);
        Assert.Equal(HttpStatusCode.OK, (await SaveHoursAsync(_client, s.Manager, s.DoctorId, s.A, Period(0, "09:00", "13:00"))).StatusCode);

        // Saving the inactive one does not look at the active clinic ...
        var inactive = await SaveHoursAsync(_client, s.Manager, s.DoctorId, s.B, Period(0, "10:00", "12:00"));
        Assert.Equal(HttpStatusCode.OK, inactive.StatusCode);
        Assert.False((await ProblemAsync(inactive)).GetProperty("isActive").GetBoolean());

        // ... and the active one does not look at the inactive clinic's stored periods.
        var active = await SaveHoursAsync(_client, s.Manager, s.DoctorId, s.A, Period(0, "09:00", "14:00"));
        Assert.Equal(HttpStatusCode.OK, active.StatusCode);
        Assert.Equal(["0 10:00:00-12:00:00"], PeriodTexts(await GetHoursOkAsync(_client, s.Manager, s.DoctorId, s.B)));
    }

    [Fact]
    public async Task A_deleted_clinics_periods_are_ignored()
    {
        var s = await SetupAsync();
        Assert.Equal(HttpStatusCode.OK, (await SaveHoursAsync(_client, s.Manager, s.DoctorId, s.B, Period(0, "09:00", "13:00"))).StatusCode);
        await SoftDeleteClinicAsync(_fixture.Factory.Services, s.B);

        var response = await SaveHoursAsync(_client, s.Manager, s.DoctorId, s.A, Period(0, "09:00", "13:00"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
