using System.Net;
using ClinicBooking.Tests.Support;
using static ClinicBooking.Tests.Support.AuthHelpers;
using static ClinicBooking.Tests.Support.DoctorHelpers;
using static ClinicBooking.Tests.Support.SpecialtyHelpers;

namespace ClinicBooking.Tests;

/// <summary>
/// Clinic assignments (D61): added active, deactivated and reactivated, never deleted. Every change needs
/// doctors.manage in that clinic; authorization over the doctor counts inactive assignments too.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class DoctorsAssignmentsTests : IClassFixture<AuthApiFixture>
{
    private readonly AuthApiFixture _fixture;
    private readonly HttpClient _client;

    public DoctorsAssignmentsTests(AuthApiFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Factory.CreateClient();
    }

    private sealed record Setup(long A, long B, long Specialty, long DoctorId, string ManagerAB);

    /// <summary>A doctor in clinics A and B, both active, and a manager of both.</summary>
    private async Task<Setup> SetupAsync()
    {
        var a = await NewClinicAsync(_fixture, _client);
        var b = await NewClinicAsync(_fixture, _client);
        var specialty = await NewSpecialtyAsync(_fixture, _client);
        var (manager, _) = await DoctorManagerAsync(_fixture, _client, a, b);
        var doctor = await CreateDoctorOkAsync(_client, manager, [specialty], [a, b]);
        return new Setup(a, b, specialty, doctor.GetProperty("id").GetInt64(), manager);
    }

    private Task<HttpResponseMessage> PostAsync(string token, long doctorId, long clinicId, string action = "") =>
        SendAsync(_client, HttpMethod.Post, $"/api/doctors/{doctorId}/clinics/{clinicId}{action}", token);

    // ---- add --------------------------------------------------------------------------------

    [Fact]
    public async Task A_manager_of_a_clinic_adds_a_doctor_there_as_active()
    {
        var s = await SetupAsync();
        var x = await NewClinicAsync(_fixture, _client);
        var (managerX, _) = await DoctorManagerAsync(_fixture, _client, x);

        var response = await PostAsync(managerX, s.DoctorId, x);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { (s.A, true), (s.B, true), (x, true) }.OrderBy(c => c.Item1).ToArray(), Assignments(await ProblemAsync(response)));

        // Now one of the doctor's clinics: the manager of X may edit the doctor.
        Assert.Equal(HttpStatusCode.OK, (await UpdateDoctorAsync(_client, managerX, await GetDoctorAsync(_client, managerX, s.DoctorId))).StatusCode);
    }

    [Fact]
    public async Task Adding_needs_doctors_manage_in_the_target_clinic()
    {
        var s = await SetupAsync();
        var x = await NewClinicAsync(_fixture, _client);
        var (nobody, _) = await SignInAsync(_fixture, _client);

        await AssertProblemAsync(await PostAsync(s.ManagerAB, s.DoctorId, x), HttpStatusCode.Forbidden, "error.auth.forbidden");
        await AssertProblemAsync(await PostAsync(nobody, s.DoctorId, x), HttpStatusCode.Forbidden, "error.auth.forbidden");
        await AssertProblemAsync(await _client.PostAsync($"/api/doctors/{s.DoctorId}/clinics/{x}", null), HttpStatusCode.Unauthorized, "error.auth.unauthorized");
    }

    [Fact]
    public async Task Adding_to_a_deleted_clinic_is_403_and_an_unknown_doctor_is_404()
    {
        var s = await SetupAsync();
        var doomed = await NewClinicAsync(_fixture, _client);
        var (manager, _) = await DoctorManagerAsync(_fixture, _client, doomed);
        await SoftDeleteClinicAsync(_fixture.Factory.Services, doomed);
        var x = await NewClinicAsync(_fixture, _client);
        var (managerX, _) = await DoctorManagerAsync(_fixture, _client, x);

        await AssertProblemAsync(await PostAsync(manager, s.DoctorId, doomed), HttpStatusCode.Forbidden, "error.auth.forbidden");
        await AssertProblemAsync(await PostAsync(managerX, 987654321, x), HttpStatusCode.NotFound, "error.doctor.not_found");
    }

    [Fact]
    public async Task An_existing_assignment_active_or_not_is_409()
    {
        var s = await SetupAsync();

        await AssertProblemAsync(await PostAsync(s.ManagerAB, s.DoctorId, s.A), HttpStatusCode.Conflict, "error.doctor.clinic_already_assigned");

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(s.ManagerAB, s.DoctorId, s.B, "/deactivate")).StatusCode);
        await AssertProblemAsync(await PostAsync(s.ManagerAB, s.DoctorId, s.B), HttpStatusCode.Conflict, "error.doctor.clinic_already_assigned");
    }

    // ---- deactivate and reactivate ----------------------------------------------------------

    [Fact]
    public async Task Deactivating_keeps_the_assignment_and_its_hours_and_is_idempotent()
    {
        var s = await SetupAsync();
        Assert.Equal(HttpStatusCode.OK, (await SaveHoursAsync(_client, s.ManagerAB, s.DoctorId, s.B, Period(2, "09:00", "12:00"))).StatusCode);

        var first = await PostAsync(s.ManagerAB, s.DoctorId, s.B, "/deactivate");
        var again = await PostAsync(s.ManagerAB, s.DoctorId, s.B, "/deactivate");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(new[] { (s.A, true), (s.B, false) }.OrderBy(c => c.Item1).ToArray(), Assignments(await ProblemAsync(again)));
        var hours = await GetHoursOkAsync(_client, s.ManagerAB, s.DoctorId, s.B);
        Assert.False(hours.GetProperty("isActive").GetBoolean());
        Assert.Equal(["2 09:00:00-12:00:00"], PeriodTexts(hours));
    }

    [Fact]
    public async Task Activate_and_deactivate_need_doctors_manage_in_that_clinic()
    {
        var s = await SetupAsync();
        var (managerA, _) = await DoctorManagerAsync(_fixture, _client, s.A);

        await AssertProblemAsync(await PostAsync(managerA, s.DoctorId, s.B, "/deactivate"), HttpStatusCode.Forbidden, "error.auth.forbidden");
        await AssertProblemAsync(await PostAsync(managerA, s.DoctorId, s.B, "/activate"), HttpStatusCode.Forbidden, "error.auth.forbidden");
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(managerA, s.DoctorId, s.A, "/deactivate")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(managerA, s.DoctorId, s.A, "/activate")).StatusCode);
    }

    [Fact]
    public async Task Activate_and_deactivate_in_an_unassigned_clinic_are_404()
    {
        var s = await SetupAsync();
        var x = await NewClinicAsync(_fixture, _client);
        var (managerX, _) = await DoctorManagerAsync(_fixture, _client, x);

        await AssertProblemAsync(await PostAsync(managerX, s.DoctorId, x, "/deactivate"), HttpStatusCode.NotFound, "error.doctor.clinic_not_assigned");
        await AssertProblemAsync(await PostAsync(managerX, s.DoctorId, x, "/activate"), HttpStatusCode.NotFound, "error.doctor.clinic_not_assigned");
        await AssertProblemAsync(await PostAsync(managerX, 987654321, x, "/activate"), HttpStatusCode.NotFound, "error.doctor.not_found");
    }

    [Fact]
    public async Task Reactivating_reruns_the_cross_clinic_check_and_is_refused_when_the_kept_hours_now_overlap()
    {
        var s = await SetupAsync();
        Assert.Equal(HttpStatusCode.OK, (await SaveHoursAsync(_client, s.ManagerAB, s.DoctorId, s.B, Period(0, "09:00", "13:00"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(s.ManagerAB, s.DoctorId, s.B, "/deactivate")).StatusCode);
        // While B is inactive, A takes the same morning.
        Assert.Equal(HttpStatusCode.OK, (await SaveHoursAsync(_client, s.ManagerAB, s.DoctorId, s.A, Period(0, "10:00", "12:00"))).StatusCode);

        var refused = await PostAsync(s.ManagerAB, s.DoctorId, s.B, "/activate");

        await AssertProblemAsync(refused, HttpStatusCode.UnprocessableEntity, "error.doctor.period_overlaps_other_clinic");
        var refusedBody = await ProblemAsync(refused);
        Assert.Equal(s.A, refusedBody.GetProperty("conflictingClinicId").GetInt64());
        Assert.False(refusedBody.TryGetProperty("periodIndex", out _)); // no list was sent
        Assert.Contains((s.B, false), Assignments(await GetDoctorAsync(_client, s.ManagerAB, s.DoctorId)));

        // Once B's kept hours no longer clash, reactivation succeeds.
        Assert.Equal(HttpStatusCode.OK, (await SaveHoursAsync(_client, s.ManagerAB, s.DoctorId, s.B, Period(0, "13:00", "15:00"))).StatusCode);
        var accepted = await PostAsync(s.ManagerAB, s.DoctorId, s.B, "/activate");
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Contains((s.B, true), Assignments(await ProblemAsync(accepted)));
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(s.ManagerAB, s.DoctorId, s.B, "/activate")).StatusCode); // idempotent
    }

    [Fact]
    public async Task Activating_or_deactivating_moves_the_assignments_row_version()
    {
        var s = await SetupAsync();
        var before = (await GetHoursOkAsync(_client, s.ManagerAB, s.DoctorId, s.A)).GetProperty("rowVersion").GetString();

        Assert.Equal(HttpStatusCode.OK, (await PostAsync(s.ManagerAB, s.DoctorId, s.A, "/deactivate")).StatusCode);

        var stale = await PutHoursAsync(_client, s.ManagerAB, s.DoctorId, s.A, [], before);
        await AssertProblemAsync(stale, HttpStatusCode.Conflict, "error.concurrency.conflict");
    }

    // ---- inactive assignments still count ---------------------------------------------------

    [Fact]
    public async Task A_doctor_with_every_assignment_inactive_stays_manageable_by_its_clinics()
    {
        var s = await SetupAsync();
        var (managerA, _) = await DoctorManagerAsync(_fixture, _client, s.A);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(s.ManagerAB, s.DoctorId, s.A, "/deactivate")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(s.ManagerAB, s.DoctorId, s.B, "/deactivate")).StatusCode);

        var doctor = await GetDoctorAsync(_client, managerA, s.DoctorId);
        Assert.All(Assignments(doctor), c => Assert.False(c.IsActive));
        Assert.Equal(HttpStatusCode.OK, (await UpdateDoctorAsync(_client, managerA, doctor)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(managerA, s.DoctorId, s.A, "/activate")).StatusCode);
    }

    [Fact]
    public async Task Delete_needs_every_clinic_including_the_inactive_ones()
    {
        var s = await SetupAsync();
        var (managerA, _) = await DoctorManagerAsync(_fixture, _client, s.A);
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(s.ManagerAB, s.DoctorId, s.B, "/deactivate")).StatusCode);

        await AssertProblemAsync(await SendAsync(_client, HttpMethod.Delete, $"/api/doctors/{s.DoctorId}", managerA), HttpStatusCode.Forbidden, "error.doctor.all_clinics_required");
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(_client, HttpMethod.Delete, $"/api/doctors/{s.DoctorId}", s.ManagerAB)).StatusCode);
    }

    [Fact]
    public async Task A_clinic_with_only_an_inactive_assignment_of_a_live_doctor_is_still_in_use()
    {
        var s = await SetupAsync();
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(s.ManagerAB, s.DoctorId, s.B, "/deactivate")).StatusCode);

        var response = await SendAsync(_client, HttpMethod.Delete, $"/api/clinics/{s.B}", await ClinicHelpers.ClinicManagerTokenAsync(_fixture, _client));

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "error.clinic.in_use");
    }

    [Fact]
    public async Task The_IsActive_filter_applies_to_the_chosen_clinic()
    {
        var s = await SetupAsync();
        var (token, _) = await SignInAsync(_fixture, _client);
        var other = (await CreateDoctorOkAsync(_client, s.ManagerAB, [s.Specialty], [s.A, s.B])).GetProperty("id").GetInt64();
        Assert.Equal(HttpStatusCode.OK, (await PostAsync(s.ManagerAB, s.DoctorId, s.B, "/deactivate")).StatusCode);

        Assert.Equal([other], Ids(await ListDoctorsAsync(_client, token, $"clinicId={s.B}&isActive=true")));
        Assert.Equal([s.DoctorId], Ids(await ListDoctorsAsync(_client, token, $"clinicId={s.B}&isActive=false")));
        Assert.Equal(new[] { s.DoctorId, other }.Order(), Ids(await ListDoctorsAsync(_client, token, $"clinicId={s.B}")).Order());
        Assert.Equal(new[] { s.DoctorId, other }.Order(), Ids(await ListDoctorsAsync(_client, token, $"clinicId={s.A}&isActive=true")).Order());
    }
}
