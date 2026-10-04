using System.Net;
using ClinicBooking.Application.Interfaces;
using ClinicBooking.Domain.Entities;
using ClinicBooking.Domain.Permissions;
using ClinicBooking.Infrastructure.Persistence;
using ClinicBooking.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using static ClinicBooking.Tests.Support.AuthHelpers;
using static ClinicBooking.Tests.Support.SpecialtyHelpers;

namespace ClinicBooking.Tests;

/// <summary>
/// Clinic-scoped authorization end to end: the table, the checker, the policy handler and the
/// resource-by-id check (D34, D57), through test-only endpoints in the test assembly.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class ClinicScopedAuthorizationTests : IClassFixture<AuthApiFixture>
{
    private const string OtherPermission = "test.other"; // stored only to prove the 403-versus-404 rule

    private readonly AuthApiFixture _fixture;
    private readonly HttpClient _client;

    public ClinicScopedAuthorizationTests(AuthApiFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Factory.CreateClient();
    }

    private async Task<long> NewClinicAsync()
    {
        var manager = await ClinicHelpers.ClinicManagerTokenAsync(_fixture, _client);
        var clinic = await ClinicHelpers.CreateClinicOkAsync(_client, manager, UniqueArabic(), UniqueEnglish());
        return clinic.GetProperty("id").GetInt64();
    }

    private async Task GrantAsync(string adminToken, long userId, long clinicId, params string[] permissions)
    {
        var response = await UserApi.SendAsync(
            _client, HttpMethod.Put, $"/api/users/{userId}/clinics/{clinicId}/permissions", adminToken, new { permissions });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task AddRawRowAsync(long userId, long clinicId, string permission)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.UserClinicPermissions.Add(new UserClinicPermission { UserId = userId, ClinicId = clinicId, Permission = permission });
        await db.SaveChangesAsync();
    }

    private Task<HttpResponseMessage> GetAsync(string url, string token) =>
        UserApi.SendAsync(_client, HttpMethod.Get, url, token);

    // --- the policy: the clinic is in the route ---

    [Fact]
    public async Task A_grant_in_clinic_A_opens_clinic_A_and_not_clinic_B()
    {
        var (admin, _) = await UserApi.AdminAsync(_fixture, _client);
        var clinicA = await NewClinicAsync();
        var clinicB = await NewClinicAsync();
        var (token, userId) = await SignInAsync(_fixture, _client);
        await GrantAsync(admin, userId, clinicA, Permissions.Doctors.Manage);

        Assert.Equal(HttpStatusCode.OK, (await GetAsync($"/test/clinics/{clinicA}/doctors", token)).StatusCode);
        await AssertProblemAsync(await GetAsync($"/test/clinics/{clinicB}/doctors", token), HttpStatusCode.Forbidden, "error.auth.forbidden");
    }

    [Fact]
    public async Task Without_a_token_the_clinic_endpoint_is_401()
    {
        var clinic = await NewClinicAsync();

        await AssertProblemAsync(
            await _client.GetAsync($"/test/clinics/{clinic}/doctors"), HttpStatusCode.Unauthorized, "error.auth.unauthorized");
    }

    [Fact]
    public async Task Every_global_permission_together_does_not_satisfy_a_clinic_scoped_one()
    {
        var clinic = await NewClinicAsync();
        var (token, _) = await SignInAsync(_fixture, _client, Permissions.Global.ToArray());

        await AssertProblemAsync(await GetAsync($"/test/clinics/{clinic}/doctors", token), HttpStatusCode.Forbidden, "error.auth.forbidden");
    }

    [Fact]
    public async Task A_revoked_grant_stops_working_at_the_very_next_request()
    {
        var (admin, _) = await UserApi.AdminAsync(_fixture, _client);
        var clinic = await NewClinicAsync();
        var (token, userId) = await SignInAsync(_fixture, _client);
        await GrantAsync(admin, userId, clinic, Permissions.Doctors.Manage);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync($"/test/clinics/{clinic}/doctors", token)).StatusCode);

        await GrantAsync(admin, userId, clinic); // empty: removes every grant in this clinic

        await AssertProblemAsync(await GetAsync($"/test/clinics/{clinic}/doctors", token), HttpStatusCode.Forbidden, "error.auth.forbidden");
    }

    [Fact]
    public async Task A_grant_in_a_soft_deleted_clinic_grants_nothing_and_is_not_listed()
    {
        var (admin, _) = await UserApi.AdminAsync(_fixture, _client);
        var manager = await ClinicHelpers.ClinicManagerTokenAsync(_fixture, _client);
        var kept = await NewClinicAsync();
        var doomed = await NewClinicAsync();
        var (token, userId) = await SignInAsync(_fixture, _client);
        await GrantAsync(admin, userId, kept, Permissions.Doctors.Manage);
        await GrantAsync(admin, userId, doomed, Permissions.Doctors.Manage);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync($"/test/clinics/{doomed}/doctors", token)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await UserApi.SendAsync(_client, HttpMethod.Delete, $"/api/clinics/{doomed}", manager)).StatusCode);

        await AssertProblemAsync(await GetAsync($"/test/clinics/{doomed}/doctors", token), HttpStatusCode.Forbidden, "error.auth.forbidden");
        Assert.Equal(HttpStatusCode.OK, (await GetAsync($"/test/clinics/{kept}/doctors", token)).StatusCode);

        var detail = (await UserApi.DetailAsync(_client, admin, userId)).GetProperty("clinicPermissions");
        Assert.Equal([kept], detail.EnumerateArray().Select(c => c.GetProperty("clinicId").GetInt64()).ToArray());
        var me = (await ProblemAsync(await GetAsync("/api/auth/me", token))).GetProperty("clinicPermissions");
        Assert.Equal([kept], me.EnumerateArray().Select(c => c.GetProperty("clinicId").GetInt64()).ToArray());

        using var scope = _fixture.Factory.Services.CreateScope();
        var checker = scope.ServiceProvider.GetRequiredService<IPermissionChecker>();
        Assert.False(await checker.HasClinicPermissionAsync(userId, doomed, Permissions.Doctors.Manage, CancellationToken.None));
        Assert.Equal([kept], await checker.GetClinicIdsWithPermissionAsync(userId, Permissions.Doctors.Manage, CancellationToken.None));
    }

    [Fact]
    public async Task A_scoped_endpoint_whose_route_names_no_clinic_fails_closed()
    {
        var (admin, _) = await UserApi.AdminAsync(_fixture, _client);
        var clinic = await NewClinicAsync();
        var (token, userId) = await SignInAsync(_fixture, _client);
        await GrantAsync(admin, userId, clinic, Permissions.Doctors.Manage);

        await AssertProblemAsync(await GetAsync("/test/scoped-without-a-clinic", token), HttpStatusCode.Forbidden, "error.auth.forbidden");
    }

    [Fact]
    public async Task A_missing_clinic_in_the_route_is_403_for_a_policy_endpoint()
    {
        var (token, _) = await SignInAsync(_fixture, _client);

        await AssertProblemAsync(await GetAsync("/test/clinics/999999999/doctors", token), HttpStatusCode.Forbidden, "error.auth.forbidden");
    }

    // --- the checker ---

    [Fact]
    public async Task The_checker_lists_ids_and_grants_for_live_clinics_and_known_names_only()
    {
        var (admin, _) = await UserApi.AdminAsync(_fixture, _client);
        var clinicA = await NewClinicAsync();
        var clinicB = await NewClinicAsync();
        var (_, userId) = await SignInAsync(_fixture, _client);
        await GrantAsync(admin, userId, clinicA, Permissions.Doctors.Manage);
        await AddRawRowAsync(userId, clinicB, OtherPermission); // a name that is not in the code

        using var scope = _fixture.Factory.Services.CreateScope();
        var checker = scope.ServiceProvider.GetRequiredService<IPermissionChecker>();

        Assert.True(await checker.HasClinicPermissionAsync(userId, clinicA, Permissions.Doctors.Manage, CancellationToken.None));
        Assert.False(await checker.HasClinicPermissionAsync(userId, clinicB, Permissions.Doctors.Manage, CancellationToken.None));
        Assert.True(await checker.HasClinicPermissionInAnyAsync(userId, [clinicA, clinicB], Permissions.Doctors.Manage, CancellationToken.None));
        Assert.False(await checker.HasClinicPermissionInAnyAsync(userId, [clinicB], Permissions.Doctors.Manage, CancellationToken.None));
        Assert.True(await checker.HasAnyClinicPermissionInAnyAsync(userId, [clinicB], CancellationToken.None));
        Assert.False(await checker.HasAnyClinicPermissionInAnyAsync(userId, [], CancellationToken.None));
        Assert.Equal([clinicA], await checker.GetClinicIdsWithPermissionAsync(userId, Permissions.Doctors.Manage, CancellationToken.None));

        var grants = await checker.GetClinicPermissionsAsync(userId, CancellationToken.None);
        var grant = Assert.Single(grants);
        Assert.Equal(clinicA, grant.ClinicId);
        Assert.Equal(Permissions.Doctors.Manage, grant.Permission);
        Assert.False(string.IsNullOrEmpty(grant.ClinicNameAr));
        Assert.False(string.IsNullOrEmpty(grant.ClinicNameEn));
    }

    [Fact]
    public async Task A_disabled_users_clinic_grant_grants_nothing()
    {
        var (admin, _) = await UserApi.AdminAsync(_fixture, _client);
        var clinic = await NewClinicAsync();
        var (_, userId) = await SignInAsync(_fixture, _client);
        await GrantAsync(admin, userId, clinic, Permissions.Doctors.Manage);
        await UserApi.SetActiveAsync(_fixture.Factory.Services, userId, false);

        using var scope = _fixture.Factory.Services.CreateScope();
        var checker = scope.ServiceProvider.GetRequiredService<IPermissionChecker>();

        Assert.False(await checker.HasClinicPermissionAsync(userId, clinic, Permissions.Doctors.Manage, CancellationToken.None));
        Assert.False(await checker.HasClinicPermissionInAnyAsync(userId, [clinic], Permissions.Doctors.Manage, CancellationToken.None));
        Assert.False(await checker.HasAnyClinicPermissionInAnyAsync(userId, [clinic], CancellationToken.None));
        Assert.Empty(await checker.GetClinicIdsWithPermissionAsync(userId, Permissions.Doctors.Manage, CancellationToken.None));
    }

    // --- resources addressed by their own id (the 404-versus-403 rule) ---

    [Fact]
    public async Task A_resource_by_id_is_200_with_the_permission_in_any_of_its_clinics()
    {
        var (admin, _) = await UserApi.AdminAsync(_fixture, _client);
        var clinicA = await NewClinicAsync();
        var clinicB = await NewClinicAsync();
        var (token, userId) = await SignInAsync(_fixture, _client);
        await GrantAsync(admin, userId, clinicA, Permissions.Doctors.Manage);
        var inA = 7_000_000 + Random.Shared.Next(100_000);
        ClinicScopedController.Resources[inA] = [clinicA];
        ClinicScopedController.Resources[inA + 1] = [clinicA, clinicB]; // a resource shared by two clinics

        Assert.Equal(HttpStatusCode.OK, (await GetAsync($"/test/resources/{inA}", token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetAsync($"/test/resources/{inA + 1}", token)).StatusCode);
    }

    [Fact]
    public async Task A_resource_in_a_clinic_where_the_caller_holds_nothing_looks_exactly_like_a_missing_one()
    {
        var (admin, _) = await UserApi.AdminAsync(_fixture, _client);
        var clinicA = await NewClinicAsync();
        var clinicB = await NewClinicAsync();
        var (token, userId) = await SignInAsync(_fixture, _client);
        await GrantAsync(admin, userId, clinicA, Permissions.Doctors.Manage);
        var inB = 7_200_000 + Random.Shared.Next(100_000);
        ClinicScopedController.Resources[inB] = [clinicB];

        var hidden = await GetAsync($"/test/resources/{inB}", token);
        var missing = await GetAsync("/test/resources/999999999", token);

        await AssertProblemAsync(hidden, HttpStatusCode.NotFound, ClinicScopedController.ResourceNotFoundKey);
        Assert.Equal(await UserApi.ComparableAsync(missing), await UserApi.ComparableAsync(hidden));
    }

    [Fact]
    public async Task A_resource_is_404_for_a_user_with_no_grants_at_all_and_for_a_global_administrator()
    {
        var clinic = await NewClinicAsync();
        var resource = 7_400_000 + Random.Shared.Next(100_000);
        ClinicScopedController.Resources[resource] = [clinic];
        var (nobody, _) = await SignInAsync(_fixture, _client);
        var (everything, _) = await SignInAsync(_fixture, _client, Permissions.Global.ToArray());

        await AssertProblemAsync(await GetAsync($"/test/resources/{resource}", nobody), HttpStatusCode.NotFound, ClinicScopedController.ResourceNotFoundKey);
        await AssertProblemAsync(await GetAsync($"/test/resources/{resource}", everything), HttpStatusCode.NotFound, ClinicScopedController.ResourceNotFoundKey);
    }

    [Fact]
    public async Task A_resource_is_403_when_the_caller_holds_some_other_permission_in_its_clinic()
    {
        var clinic = await NewClinicAsync();
        var resource = 7_600_000 + Random.Shared.Next(100_000);
        ClinicScopedController.Resources[resource] = [clinic];
        var (token, userId) = await SignInAsync(_fixture, _client);
        await AddRawRowAsync(userId, clinic, OtherPermission);

        await AssertProblemAsync(await GetAsync($"/test/resources/{resource}", token), HttpStatusCode.Forbidden, "error.auth.forbidden");
    }

    [Fact]
    public async Task A_resource_by_id_is_401_without_a_token()
    {
        await AssertProblemAsync(
            await _client.GetAsync("/test/resources/1"), HttpStatusCode.Unauthorized, "error.auth.unauthorized");
    }

    [Fact]
    public async Task A_resource_whose_clinic_was_deleted_is_404_for_a_former_grantee()
    {
        var (admin, _) = await UserApi.AdminAsync(_fixture, _client);
        var manager = await ClinicHelpers.ClinicManagerTokenAsync(_fixture, _client);
        var clinic = await NewClinicAsync();
        var (token, userId) = await SignInAsync(_fixture, _client);
        await GrantAsync(admin, userId, clinic, Permissions.Doctors.Manage);
        var resource = 7_800_000 + Random.Shared.Next(100_000);
        ClinicScopedController.Resources[resource] = [clinic];
        Assert.Equal(HttpStatusCode.OK, (await GetAsync($"/test/resources/{resource}", token)).StatusCode);

        await UserApi.SendAsync(_client, HttpMethod.Delete, $"/api/clinics/{clinic}", manager);

        await AssertProblemAsync(await GetAsync($"/test/resources/{resource}", token), HttpStatusCode.NotFound, ClinicScopedController.ResourceNotFoundKey);
    }
}
