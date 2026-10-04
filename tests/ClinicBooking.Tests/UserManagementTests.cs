using System.Net;
using System.Text.Json;
using ClinicBooking.Domain.Permissions;
using ClinicBooking.Infrastructure.Persistence;
using ClinicBooking.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using static ClinicBooking.Tests.Support.AuthHelpers;
using static ClinicBooking.Tests.Support.SpecialtyHelpers;

namespace ClinicBooking.Tests;

[Collection(SqlServerCollection.Name)]
public class UserManagementTests : IClassFixture<AuthApiFixture>
{
    private readonly AuthApiFixture _fixture;
    private readonly HttpClient _client;

    public UserManagementTests(AuthApiFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Factory.CreateClient();
    }

    private async Task<string> AdminTokenAsync() => (await UserApi.AdminAsync(_fixture, _client)).Token;

    private async Task<long> CreateClinicAsync()
    {
        var manager = await ClinicHelpers.ClinicManagerTokenAsync(_fixture, _client);
        var clinic = await ClinicHelpers.CreateClinicOkAsync(_client, manager, UniqueArabic(), UniqueEnglish());
        return clinic.GetProperty("id").GetInt64();
    }

    [Fact]
    public async Task Creating_a_user_returns_201_with_a_detail_that_requires_a_password_change_and_stamps_the_audit_fields()
    {
        _fixture.Clock.Now = new DateTimeOffset(2026, 3, 4, 10, 0, 0, TimeSpan.Zero);
        var (admin, adminId) = await UserApi.AdminAsync(_fixture, _client);
        var userName = UserApi.NewUserName();

        var response = await UserApi.SendAsync(
            _client, HttpMethod.Post, "/api/users", admin,
            new { userName, temporaryPassword = UserApi.NewTemporaryPassword() });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ProblemAsync(response);
        var id = body.GetProperty("id").GetInt64();
        Assert.EndsWith($"/api/users/{id}", response.Headers.Location!.ToString());
        Assert.Equal(userName, body.GetProperty("userName").GetString());
        Assert.True(body.GetProperty("isActive").GetBoolean());
        Assert.True(body.GetProperty("mustChangePassword").GetBoolean());
        Assert.Empty(body.GetProperty("globalPermissions").EnumerateArray());
        Assert.Empty(body.GetProperty("clinicPermissions").EnumerateArray());

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Users.SingleAsync(u => u.Id == id);
        Assert.Equal(adminId, stored.CreatedBy);
        Assert.Equal(_fixture.Clock.Now, stored.CreatedAt);
    }

    [Fact]
    public async Task A_new_user_can_sign_in_with_the_temporary_password()
    {
        var admin = await AdminTokenAsync();
        var user = await UserApi.CreateUserAsync(_client, admin);

        var login = await LoginAsync(_client, user.UserName, user.Password);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Theory]
    [InlineData("", "error.user.user_name_required")]
    [InlineData("   ", "error.user.user_name_required")]
    [InlineData("ab", "error.user.user_name_too_short")]
    [InlineData("has space", "error.user.user_name_invalid")]
    [InlineData("name@host", "error.user.user_name_invalid")]
    [InlineData("اسم_عربي", "error.user.user_name_invalid")]
    public async Task User_name_rules_answer_error_keys_on_the_field(string userName, string key)
    {
        var admin = await AdminTokenAsync();

        var response = await UserApi.SendAsync(
            _client, HttpMethod.Post, "/api/users", admin,
            new { userName, temporaryPassword = UserApi.NewTemporaryPassword() });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "error.validation.failed");
        Assert.Equal(key, (await ProblemAsync(response)).GetProperty("errors").GetProperty("userName")[0].GetString());
    }

    [Fact]
    public async Task A_user_name_of_64_characters_is_accepted_and_65_is_too_long()
    {
        var admin = await AdminTokenAsync();
        var ok = new string('a', 60) + Guid.NewGuid().ToString("N")[..4];

        var accepted = await UserApi.SendAsync(
            _client, HttpMethod.Post, "/api/users", admin, new { userName = ok, temporaryPassword = UserApi.NewTemporaryPassword() });
        var refused = await UserApi.SendAsync(
            _client, HttpMethod.Post, "/api/users", admin, new { userName = ok + "x", temporaryPassword = UserApi.NewTemporaryPassword() });

        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        await AssertProblemAsync(refused, HttpStatusCode.BadRequest, "error.validation.failed");
        Assert.Equal(
            "error.user.user_name_too_long",
            (await ProblemAsync(refused)).GetProperty("errors").GetProperty("userName")[0].GetString());
    }

    [Theory]
    [InlineData("", "error.password.required")]
    [InlineData("Ab1", "error.password.too_short")]
    [InlineData("nouppercase-1234567", "error.password.requires_uppercase")]
    [InlineData("NOLOWERCASE-1234567", "error.password.requires_lowercase")]
    [InlineData("No-Digits-Anywhere-Here", "error.password.requires_digit")]
    [InlineData("Aa1Aa1Aa1Aa1Aa1Aa1", "error.password.requires_unique_chars")]
    public async Task The_temporary_password_must_meet_the_policy_and_the_user_is_not_created(string password, string key)
    {
        var admin = await AdminTokenAsync();
        var userName = UserApi.NewUserName();

        var response = await UserApi.SendAsync(
            _client, HttpMethod.Post, "/api/users", admin, new { userName, temporaryPassword = password });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "error.validation.failed");
        var keys = UserApi.Strings((await ProblemAsync(response)).GetProperty("errors").GetProperty("temporaryPassword"));
        Assert.Contains(key, keys);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Users.AnyAsync(u => u.UserName == userName));
    }

    [Fact]
    public async Task A_temporary_password_of_129_characters_is_too_long()
    {
        var admin = await AdminTokenAsync();
        var password = "Aa1-" + new string('x', 125);

        var response = await UserApi.SendAsync(
            _client, HttpMethod.Post, "/api/users", admin, new { userName = UserApi.NewUserName(), temporaryPassword = password });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "error.validation.failed");
        Assert.Equal(
            "error.password.too_long",
            (await ProblemAsync(response)).GetProperty("errors").GetProperty("temporaryPassword")[0].GetString());
    }

    [Fact]
    public async Task A_user_name_is_unique_ignoring_case()
    {
        var admin = await AdminTokenAsync();
        var first = await UserApi.CreateUserAsync(_client, admin);

        var response = await UserApi.SendAsync(
            _client, HttpMethod.Post, "/api/users", admin,
            new { userName = first.UserName.ToUpperInvariant(), temporaryPassword = UserApi.NewTemporaryPassword() });

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "error.user.user_name_taken");
    }

    [Fact]
    public async Task Getting_an_unknown_user_is_404()
    {
        var admin = await AdminTokenAsync();

        var response = await UserApi.SendAsync(_client, HttpMethod.Get, "/api/users/999999999", admin);

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "error.user.not_found");
    }

    [Fact]
    public async Task The_list_searches_pages_filters_and_sorts()
    {
        var admin = await AdminTokenAsync();
        var tag = Guid.NewGuid().ToString("N")[..8];
        var names = new List<string>();
        foreach (var suffix in new[] { "alpha", "bravo", "charlie" })
        {
            var userName = $"zz{tag}-{suffix}";
            var created = await UserApi.SendAsync(
                _client, HttpMethod.Post, "/api/users", admin,
                new { userName, temporaryPassword = UserApi.NewTemporaryPassword() });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            names.Add(userName);
        }

        var bravoId = (await ProblemAsync(await UserApi.SendAsync(_client, HttpMethod.Get, $"/api/users?search={tag.ToUpperInvariant()}-BRAVO", admin)))
            .GetProperty("items")[0].GetProperty("id").GetInt64();
        await UserApi.SetActiveAsync(_fixture.Factory.Services, bravoId, false);

        async Task<JsonElement> ListAsync(string query)
        {
            var response = await UserApi.SendAsync(_client, HttpMethod.Get, "/api/users?" + query, admin);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await ProblemAsync(response);
        }

        string[] UserNames(JsonElement page) =>
            page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("userName").GetString()!).ToArray();

        // Case-insensitive part-of-name search, default order by user name.
        var found = await ListAsync($"search={tag.ToUpperInvariant()}");
        Assert.Equal(names.Order(StringComparer.Ordinal).ToArray(), UserNames(found));
        Assert.Equal(3, found.GetProperty("totalCount").GetInt32());

        // Paging.
        var firstPage = await ListAsync($"search={tag}&pageSize=2&page=1");
        var secondPage = await ListAsync($"search={tag}&pageSize=2&page=2");
        Assert.Equal(2, firstPage.GetProperty("items").GetArrayLength());
        Assert.Single(secondPage.GetProperty("items").EnumerateArray());
        Assert.Equal(3, secondPage.GetProperty("totalCount").GetInt32());

        // Filter on the state.
        Assert.Equal([$"zz{tag}-bravo"], UserNames(await ListAsync($"search={tag}&isActive=false")));
        Assert.Equal(2, (await ListAsync($"search={tag}&isActive=true")).GetProperty("totalCount").GetInt32());

        // Sorting.
        Assert.Equal(
            names.Order(StringComparer.Ordinal).Reverse().ToArray(),
            UserNames(await ListAsync($"search={tag}&sortBy=userName&sortDirection=desc")));
        Assert.Equal(
            names.ToArray(),
            UserNames(await ListAsync($"search={tag}&sortBy=createdAt&sortDirection=asc")));

        // Nothing sensitive is listed.
        var item = found.GetProperty("items")[0];
        Assert.Equal(
            ["id", "userName", "isActive", "mustChangePassword", "createdAt"],
            item.EnumerateObject().Select(p => p.Name).ToArray());
    }

    [Theory]
    [InlineData("page=0", "error.paging.page_invalid")]
    [InlineData("pageSize=101", "error.paging.page_size_invalid")]
    [InlineData("sortBy=passwordHash", "error.sort.invalid")]
    [InlineData("sortDirection=sideways", "error.sort.invalid")]
    public async Task The_list_rejects_bad_query_values_with_error_keys(string query, string key)
    {
        var admin = await AdminTokenAsync();

        var response = await UserApi.SendAsync(_client, HttpMethod.Get, "/api/users?" + query, admin);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "error.validation.failed");
        var errors = (await ProblemAsync(response)).GetProperty("errors");
        Assert.Contains(key, errors.EnumerateObject().SelectMany(p => UserApi.Strings(p.Value)));
    }

    [Fact]
    public async Task Replacing_global_permissions_takes_effect_on_the_very_next_request()
    {
        var admin = await AdminTokenAsync();
        var (userToken, userId) = await SignInAsync(_fixture, _client);
        var body = new { nameAr = UniqueArabic(), nameEn = UniqueEnglish(), address = (string?)null, phone = (string?)null };

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await UserApi.SendAsync(_client, HttpMethod.Post, "/api/clinics", userToken, body)).StatusCode);

        var grant = await UserApi.SendAsync(
            _client, HttpMethod.Put, $"/api/users/{userId}/global-permissions", admin,
            new { permissions = new[] { Permissions.Clinics.Manage } });
        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);
        Assert.Equal([Permissions.Clinics.Manage], UserApi.Strings((await ProblemAsync(grant)).GetProperty("globalPermissions")));

        // The same access token, no new login.
        Assert.Equal(
            HttpStatusCode.Created,
            (await UserApi.SendAsync(_client, HttpMethod.Post, "/api/clinics", userToken, body)).StatusCode);

        var revoke = await UserApi.SendAsync(
            _client, HttpMethod.Put, $"/api/users/{userId}/global-permissions", admin, new { permissions = Array.Empty<string>() });
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);

        await AssertProblemAsync(
            await UserApi.SendAsync(
                _client, HttpMethod.Post, "/api/clinics", userToken,
                new { nameAr = UniqueArabic(), nameEn = UniqueEnglish(), address = (string?)null, phone = (string?)null }),
            HttpStatusCode.Forbidden,
            "error.auth.forbidden");
    }

    [Fact]
    public async Task Duplicated_global_permission_names_are_stored_once()
    {
        var admin = await AdminTokenAsync();
        var (_, userId) = await SignInAsync(_fixture, _client);

        var response = await UserApi.SendAsync(
            _client, HttpMethod.Put, $"/api/users/{userId}/global-permissions", admin,
            new { permissions = new[] { Permissions.Specialties.Manage, Permissions.Specialties.Manage } });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([Permissions.Specialties.Manage], UserApi.Strings((await ProblemAsync(response)).GetProperty("globalPermissions")));
    }

    [Theory]
    [InlineData("doctors.manage")] // clinic-scoped: not assignable globally
    [InlineData("nonsense")]
    [InlineData("")]
    public async Task An_unknown_or_wrongly_scoped_global_permission_is_a_400(string permission)
    {
        var admin = await AdminTokenAsync();
        var (_, userId) = await SignInAsync(_fixture, _client);

        var response = await UserApi.SendAsync(
            _client, HttpMethod.Put, $"/api/users/{userId}/global-permissions", admin, new { permissions = new[] { permission } });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "error.validation.failed");
        Assert.Equal(
            "error.user.permission_unknown",
            (await ProblemAsync(response)).GetProperty("errors").GetProperty("permissions")[0].GetString());
    }

    [Fact]
    public async Task A_missing_permissions_list_is_a_400()
    {
        var admin = await AdminTokenAsync();
        var (_, userId) = await SignInAsync(_fixture, _client);

        var response = await UserApi.SendAsync(
            _client, HttpMethod.Put, $"/api/users/{userId}/global-permissions", admin, new { });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "error.validation.failed");
    }

    [Fact]
    public async Task Replacing_the_permissions_of_an_unknown_user_is_404()
    {
        var admin = await AdminTokenAsync();
        var clinicId = await CreateClinicAsync();

        await AssertProblemAsync(
            await UserApi.SendAsync(
                _client, HttpMethod.Put, "/api/users/999999999/global-permissions", admin, new { permissions = Array.Empty<string>() }),
            HttpStatusCode.NotFound,
            "error.user.not_found");
        await AssertProblemAsync(
            await UserApi.SendAsync(
                _client, HttpMethod.Put, $"/api/users/999999999/clinics/{clinicId}/permissions", admin, new { permissions = Array.Empty<string>() }),
            HttpStatusCode.NotFound,
            "error.user.not_found");
    }

    [Fact]
    public async Task Clinic_permissions_are_replaced_per_clinic_and_shown_in_the_detail_and_in_me()
    {
        var (admin, adminId) = await UserApi.AdminAsync(_fixture, _client);
        var clinicA = await CreateClinicAsync();
        var clinicB = await CreateClinicAsync();
        var (userToken, userId) = await SignInAsync(_fixture, _client);

        var grant = await UserApi.SendAsync(
            _client, HttpMethod.Put, $"/api/users/{userId}/clinics/{clinicA}/permissions", admin,
            new { permissions = new[] { Permissions.Doctors.Manage } });
        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);

        var detail = await UserApi.DetailAsync(_client, admin, userId);
        var clinics = detail.GetProperty("clinicPermissions");
        Assert.Equal(1, clinics.GetArrayLength());
        Assert.Equal(clinicA, clinics[0].GetProperty("clinicId").GetInt64());
        Assert.False(string.IsNullOrEmpty(clinics[0].GetProperty("clinicNameAr").GetString()));
        Assert.False(string.IsNullOrEmpty(clinics[0].GetProperty("clinicNameEn").GetString()));
        Assert.Equal([Permissions.Doctors.Manage], UserApi.Strings(clinics[0].GetProperty("permissions")));

        var me = await ProblemAsync(await UserApi.SendAsync(_client, HttpMethod.Get, "/api/auth/me", userToken));
        Assert.Equal(clinicA, me.GetProperty("clinicPermissions")[0].GetProperty("clinicId").GetInt64());
        Assert.False(me.GetProperty("mustChangePassword").GetBoolean());

        // The grant is audited: who granted it.
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.UserClinicPermissions.SingleAsync(p => p.UserId == userId && p.ClinicId == clinicA);
            Assert.Equal(adminId, row.CreatedBy);
        }

        // Another clinic is separate: replacing there leaves clinic A alone.
        await UserApi.SendAsync(
            _client, HttpMethod.Put, $"/api/users/{userId}/clinics/{clinicB}/permissions", admin,
            new { permissions = new[] { Permissions.Doctors.Manage } });
        await UserApi.SendAsync(
            _client, HttpMethod.Put, $"/api/users/{userId}/clinics/{clinicB}/permissions", admin,
            new { permissions = Array.Empty<string>() });
        var after = (await UserApi.DetailAsync(_client, admin, userId)).GetProperty("clinicPermissions");
        Assert.Equal(1, after.GetArrayLength());
        Assert.Equal(clinicA, after[0].GetProperty("clinicId").GetInt64());

        // Replacing with the same list changes nothing; an empty list removes the grant.
        await UserApi.SendAsync(
            _client, HttpMethod.Put, $"/api/users/{userId}/clinics/{clinicA}/permissions", admin,
            new { permissions = new[] { Permissions.Doctors.Manage } });
        Assert.Equal(1, (await UserApi.DetailAsync(_client, admin, userId)).GetProperty("clinicPermissions").GetArrayLength());
        await UserApi.SendAsync(
            _client, HttpMethod.Put, $"/api/users/{userId}/clinics/{clinicA}/permissions", admin,
            new { permissions = Array.Empty<string>() });
        Assert.Equal(0, (await UserApi.DetailAsync(_client, admin, userId)).GetProperty("clinicPermissions").GetArrayLength());
    }

    [Theory]
    [InlineData("clinics.manage")] // global: not assignable per clinic
    [InlineData("nonsense")]
    public async Task An_unknown_or_wrongly_scoped_clinic_permission_is_a_400(string permission)
    {
        var admin = await AdminTokenAsync();
        var clinicId = await CreateClinicAsync();
        var (_, userId) = await SignInAsync(_fixture, _client);

        var response = await UserApi.SendAsync(
            _client, HttpMethod.Put, $"/api/users/{userId}/clinics/{clinicId}/permissions", admin,
            new { permissions = new[] { permission } });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "error.validation.failed");
        Assert.Equal(
            "error.user.permission_unknown",
            (await ProblemAsync(response)).GetProperty("errors").GetProperty("permissions")[0].GetString());
    }

    [Fact]
    public async Task Assigning_clinic_permissions_for_a_missing_clinic_is_404_clinic_not_found()
    {
        var admin = await AdminTokenAsync();
        var (_, userId) = await SignInAsync(_fixture, _client);

        var response = await UserApi.SendAsync(
            _client, HttpMethod.Put, $"/api/users/{userId}/clinics/999999999/permissions", admin,
            new { permissions = new[] { Permissions.Doctors.Manage } });

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "error.clinic.not_found");
    }

    [Fact]
    public async Task Assigning_clinic_permissions_for_a_soft_deleted_clinic_is_404_clinic_not_found()
    {
        var admin = await AdminTokenAsync();
        var manager = await ClinicHelpers.ClinicManagerTokenAsync(_fixture, _client);
        var clinicId = await CreateClinicAsync();
        var (_, userId) = await SignInAsync(_fixture, _client);

        var deleted = await UserApi.SendAsync(_client, HttpMethod.Delete, $"/api/clinics/{clinicId}", manager);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        var response = await UserApi.SendAsync(
            _client, HttpMethod.Put, $"/api/users/{userId}/clinics/{clinicId}/permissions", admin,
            new { permissions = new[] { Permissions.Doctors.Manage } });

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "error.clinic.not_found");
    }

    [Fact]
    public async Task The_assignable_permissions_come_from_the_same_constants_as_the_policies()
    {
        var admin = await AdminTokenAsync();

        var response = await UserApi.SendAsync(_client, HttpMethod.Get, "/api/permissions", admin);

        var body = await ProblemAsync(response);
        Assert.Equal(Permissions.Global.ToArray(), UserApi.Strings(body.GetProperty("global")));
        Assert.Equal(Permissions.ClinicScoped.ToArray(), UserApi.Strings(body.GetProperty("clinicScoped")));
        Assert.Equal([Permissions.Doctors.Manage], UserApi.Strings(body.GetProperty("clinicScoped")));
    }

    [Fact]
    public async Task Users_are_never_deleted_there_is_no_delete_endpoint()
    {
        var admin = await AdminTokenAsync();
        var user = await UserApi.CreateUserAsync(_client, admin);

        var response = await UserApi.SendAsync(_client, HttpMethod.Delete, $"/api/users/{user.Id}", admin);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
    }
}
