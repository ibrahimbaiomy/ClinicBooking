using System.Net;
using ClinicBooking.Domain.Permissions;
using ClinicBooking.Tests.Support;
using static ClinicBooking.Tests.Support.AuthHelpers;
using static ClinicBooking.Tests.Support.DoctorHelpers;
using static ClinicBooking.Tests.Support.SpecialtyHelpers;

namespace ClinicBooking.Tests;

/// <summary>
/// Who may do what to a doctor (D6, D57, D61): reading is open to any signed-in user; create needs
/// doctors.manage in every requested clinic, edit in any of the doctor's clinics (404 when none), delete
/// in all of them (403 error.doctor.all_clinics_required when only some).
/// </summary>
[Collection(SqlServerCollection.Name)]
public class DoctorsAuthorizationTests : IClassFixture<AuthApiFixture>
{
    private const string OtherPermission = "test.other"; // stored only to prove the 403-versus-404 rule

    private readonly AuthApiFixture _fixture;
    private readonly HttpClient _client;

    public DoctorsAuthorizationTests(AuthApiFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Factory.CreateClient();
    }

    private sealed record TwoClinics(long A, long B, long Specialty, string ManagerAB, long DoctorId, System.Text.Json.JsonElement Doctor);

    /// <summary>Clinics A and B and a doctor assigned to both, created by a manager of both.</summary>
    private async Task<TwoClinics> DoctorInTwoClinicsAsync()
    {
        var a = await NewClinicAsync(_fixture, _client);
        var b = await NewClinicAsync(_fixture, _client);
        var specialty = await NewSpecialtyAsync(_fixture, _client);
        var (manager, _) = await DoctorManagerAsync(_fixture, _client, a, b);
        var doctor = await CreateDoctorOkAsync(_client, manager, [specialty], [a, b]);
        return new TwoClinics(a, b, specialty, manager, doctor.GetProperty("id").GetInt64(), doctor);
    }

    // ---- reading ----------------------------------------------------------------------------

    [Fact]
    public async Task Any_signed_in_user_reads_the_list_and_a_doctor_without_any_permission()
    {
        var d = await DoctorInTwoClinicsAsync();
        var (nobody, _) = await SignInAsync(_fixture, _client);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(_client, HttpMethod.Get, "/api/doctors", nobody)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(_client, HttpMethod.Get, $"/api/doctors/{d.DoctorId}", nobody)).StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/doctors")]
    [InlineData("GET", "/api/doctors/1")]
    [InlineData("POST", "/api/doctors")]
    [InlineData("PUT", "/api/doctors/1")]
    [InlineData("DELETE", "/api/doctors/1")]
    public async Task Every_endpoint_is_401_without_a_token(string method, string url)
    {
        var response = await _client.SendAsync(Json(new HttpMethod(method), url, null, method is "POST" or "PUT" ? new { } : null));

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "error.auth.unauthorized");
    }

    // ---- create -----------------------------------------------------------------------------

    [Fact]
    public async Task Create_needs_doctors_manage_in_every_requested_clinic()
    {
        var a = await NewClinicAsync(_fixture, _client);
        var b = await NewClinicAsync(_fixture, _client);
        var specialty = await NewSpecialtyAsync(_fixture, _client);
        var (managerA, _) = await DoctorManagerAsync(_fixture, _client, a);
        var (managerAB, _) = await DoctorManagerAsync(_fixture, _client, a, b);
        var (nobody, _) = await SignInAsync(_fixture, _client);
        var (everyGlobal, _) = await SignInAsync(_fixture, _client, Permissions.Global.ToArray());

        await AssertProblemAsync(await CreateDoctorAsync(_client, nobody, UniqueArabic(), UniqueEnglish(), [specialty], [a]), HttpStatusCode.Forbidden, "error.auth.forbidden");
        await AssertProblemAsync(await CreateDoctorAsync(_client, everyGlobal, UniqueArabic(), UniqueEnglish(), [specialty], [a]), HttpStatusCode.Forbidden, "error.auth.forbidden");
        await AssertProblemAsync(await CreateDoctorAsync(_client, managerA, UniqueArabic(), UniqueEnglish(), [specialty], [a, b]), HttpStatusCode.Forbidden, "error.auth.forbidden");
        Assert.Equal(HttpStatusCode.Created, (await CreateDoctorAsync(_client, managerA, UniqueArabic(), UniqueEnglish(), [specialty], [a])).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await CreateDoctorAsync(_client, managerAB, UniqueArabic(), UniqueEnglish(), [specialty], [a, b])).StatusCode);
    }

    [Fact]
    public async Task Create_in_a_missing_or_deleted_clinic_is_403()
    {
        var kept = await NewClinicAsync(_fixture, _client);
        var deleted = await NewClinicAsync(_fixture, _client);
        var specialty = await NewSpecialtyAsync(_fixture, _client);
        var (manager, _) = await DoctorManagerAsync(_fixture, _client, kept, deleted);
        await SoftDeleteClinicAsync(_fixture.Factory.Services, deleted);

        await AssertProblemAsync(await CreateDoctorAsync(_client, manager, UniqueArabic(), UniqueEnglish(), [specialty], [kept, deleted]), HttpStatusCode.Forbidden, "error.auth.forbidden");
        await AssertProblemAsync(await CreateDoctorAsync(_client, manager, UniqueArabic(), UniqueEnglish(), [specialty], [987654321]), HttpStatusCode.Forbidden, "error.auth.forbidden");
    }

    [Fact]
    public async Task A_disabled_users_grant_creates_nothing()
    {
        var clinic = await NewClinicAsync(_fixture, _client);
        var specialty = await NewSpecialtyAsync(_fixture, _client);
        var (manager, userId) = await DoctorManagerAsync(_fixture, _client, clinic);
        await UserApi.SetActiveAsync(_fixture.Factory.Services, userId, false);

        var response = await CreateDoctorAsync(_client, manager, UniqueArabic(), UniqueEnglish(), [specialty], [clinic]);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "error.auth.unauthorized");
    }

    // ---- update -----------------------------------------------------------------------------

    [Fact]
    public async Task Update_needs_doctors_manage_in_any_of_the_doctors_clinics()
    {
        var d = await DoctorInTwoClinicsAsync();
        var (managerB, _) = await DoctorManagerAsync(_fixture, _client, d.B);

        var response = await UpdateDoctorAsync(_client, managerB, d.Doctor);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Update_by_someone_who_holds_nothing_in_the_doctors_clinics_looks_like_a_missing_doctor()
    {
        var d = await DoctorInTwoClinicsAsync();
        var elsewhere = await NewClinicAsync(_fixture, _client);
        var (managerElsewhere, _) = await DoctorManagerAsync(_fixture, _client, elsewhere);
        var (nobody, _) = await SignInAsync(_fixture, _client);
        var (everyGlobal, _) = await SignInAsync(_fixture, _client, Permissions.Global.ToArray());

        foreach (var token in new[] { managerElsewhere, nobody, everyGlobal })
        {
            var hidden = await UpdateDoctorAsync(_client, token, d.Doctor);
            var missing = await UpdateDoctorAsync(_client, token, 987654321, UniqueArabic(), UniqueEnglish(), [d.Specialty], d.Doctor.GetProperty("rowVersion").GetString());

            await AssertProblemAsync(hidden, HttpStatusCode.NotFound, "error.doctor.not_found");
            Assert.Equal(await UserApi.ComparableAsync(missing), await UserApi.ComparableAsync(hidden));
        }
    }

    [Fact]
    public async Task Update_is_403_when_the_caller_holds_another_clinic_permission_there()
    {
        var d = await DoctorInTwoClinicsAsync();
        var (token, userId) = await SignInAsync(_fixture, _client);
        await GrantAsync(_fixture.Factory.Services, userId, d.A, OtherPermission);

        await AssertProblemAsync(await UpdateDoctorAsync(_client, token, d.Doctor), HttpStatusCode.Forbidden, "error.auth.forbidden");
    }

    [Fact]
    public async Task A_grant_in_a_deleted_clinic_does_not_open_the_doctor()
    {
        var a = await NewClinicAsync(_fixture, _client);
        var b = await NewClinicAsync(_fixture, _client);
        var specialty = await NewSpecialtyAsync(_fixture, _client);
        var (managerAB, _) = await DoctorManagerAsync(_fixture, _client, a, b);
        var (managerB, _) = await DoctorManagerAsync(_fixture, _client, b);
        var doctor = await CreateDoctorOkAsync(_client, managerAB, [specialty], [a, b]);
        await SoftDeleteClinicAsync(_fixture.Factory.Services, b);

        await AssertProblemAsync(await UpdateDoctorAsync(_client, managerB, doctor), HttpStatusCode.NotFound, "error.doctor.not_found");
        Assert.Equal([(a, true)], Assignments(await GetDoctorAsync(_client, managerB, doctor.GetProperty("id").GetInt64())));
    }

    // ---- delete -----------------------------------------------------------------------------

    [Fact]
    public async Task Delete_needs_doctors_manage_in_every_clinic_of_the_doctor()
    {
        var d = await DoctorInTwoClinicsAsync();
        var (managerA, _) = await DoctorManagerAsync(_fixture, _client, d.A);

        var partial = await SendAsync(_client, HttpMethod.Delete, $"/api/doctors/{d.DoctorId}", managerA);
        await AssertProblemAsync(partial, HttpStatusCode.Forbidden, "error.doctor.all_clinics_required");
        await GetDoctorAsync(_client, managerA, d.DoctorId); // still there

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(_client, HttpMethod.Delete, $"/api/doctors/{d.DoctorId}", d.ManagerAB)).StatusCode);
    }

    [Fact]
    public async Task Delete_by_someone_who_holds_nothing_in_the_doctors_clinics_is_404()
    {
        var d = await DoctorInTwoClinicsAsync();
        var (nobody, _) = await SignInAsync(_fixture, _client);

        await AssertProblemAsync(await SendAsync(_client, HttpMethod.Delete, $"/api/doctors/{d.DoctorId}", nobody), HttpStatusCode.NotFound, "error.doctor.not_found");
    }

    [Fact]
    public async Task Delete_ignores_a_deleted_clinic()
    {
        var a = await NewClinicAsync(_fixture, _client);
        var b = await NewClinicAsync(_fixture, _client);
        var specialty = await NewSpecialtyAsync(_fixture, _client);
        var (managerAB, _) = await DoctorManagerAsync(_fixture, _client, a, b);
        var (managerA, _) = await DoctorManagerAsync(_fixture, _client, a);
        var doctor = await CreateDoctorOkAsync(_client, managerAB, [specialty], [a, b]);
        await SoftDeleteClinicAsync(_fixture.Factory.Services, b);

        var response = await SendAsync(_client, HttpMethod.Delete, $"/api/doctors/{doctor.GetProperty("id").GetInt64()}", managerA);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
