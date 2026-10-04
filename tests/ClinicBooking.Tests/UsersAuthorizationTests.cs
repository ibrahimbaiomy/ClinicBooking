using System.Net;
using ClinicBooking.Domain.Permissions;
using ClinicBooking.Tests.Support;
using static ClinicBooking.Tests.Support.AuthHelpers;
using static ClinicBooking.Tests.Support.SpecialtyHelpers;

namespace ClinicBooking.Tests;

/// <summary>Every user-management endpoint needs the global users.manage and nothing else unlocks it (D57).</summary>
[Collection(SqlServerCollection.Name)]
public class UsersAuthorizationTests : IClassFixture<AuthApiFixture>
{
    private readonly AuthApiFixture _fixture;
    private readonly HttpClient _client;

    public UsersAuthorizationTests(AuthApiFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Factory.CreateClient();
    }

    private static readonly object CreateBody = new { userName = "someone", temporaryPassword = "Temp-Password-123" };
    private static readonly object PermissionsBody = new { permissions = Array.Empty<string>() };
    private static readonly object ResetBody = new { temporaryPassword = "Temp-Password-123" };

    public static TheoryData<string, string, bool> Endpoints => new()
    {
        { "GET", "/api/users", false },
        { "GET", "/api/users/1", false },
        { "POST", "/api/users", true },
        { "POST", "/api/users/1/disable", false },
        { "POST", "/api/users/1/enable", false },
        { "PUT", "/api/users/1/global-permissions", true },
        { "PUT", "/api/users/1/clinics/1/permissions", true },
        { "POST", "/api/users/1/reset-password", true },
        { "GET", "/api/permissions", false }
    };

    private static object? BodyFor(string method, string url, bool hasBody)
    {
        if (!hasBody)
        {
            return null;
        }

        return url switch
        {
            "/api/users" => CreateBody,
            var u when u.EndsWith("reset-password", StringComparison.Ordinal) => ResetBody,
            _ => PermissionsBody
        };
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Every_endpoint_is_401_without_a_token(string method, string url, bool hasBody)
    {
        var response = await _client.SendAsync(Json(new HttpMethod(method), url, null, BodyFor(method, url, hasBody)));

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "error.auth.unauthorized");
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Every_endpoint_is_403_for_a_signed_in_user_without_permissions(string method, string url, bool hasBody)
    {
        var (token, _) = await SignInAsync(_fixture, _client);

        var response = await _client.SendAsync(Json(new HttpMethod(method), url, token, BodyFor(method, url, hasBody)));

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "error.auth.forbidden");
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Every_other_global_permission_together_still_does_not_unlock_user_management(string method, string url, bool hasBody)
    {
        var others = Permissions.Global.Where(p => p != Permissions.Users.Manage).ToArray();
        var (token, _) = await SignInAsync(_fixture, _client, others);

        var response = await _client.SendAsync(Json(new HttpMethod(method), url, token, BodyFor(method, url, hasBody)));

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "error.auth.forbidden");
    }

    [Fact]
    public async Task A_clinic_scoped_permission_does_not_unlock_user_management()
    {
        var (admin, _) = await UserApi.AdminAsync(_fixture, _client);
        var manager = await ClinicHelpers.ClinicManagerTokenAsync(_fixture, _client);
        var clinic = await ClinicHelpers.CreateClinicOkAsync(_client, manager, UniqueArabic(), UniqueEnglish());
        var clinicId = clinic.GetProperty("id").GetInt64();

        var (token, userId) = await SignInAsync(_fixture, _client);
        var grant = await UserApi.SendAsync(
            _client, HttpMethod.Put, $"/api/users/{userId}/clinics/{clinicId}/permissions", admin,
            new { permissions = new[] { Permissions.Doctors.Manage } });
        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);

        var response = await UserApi.SendAsync(_client, HttpMethod.Get, "/api/users", token);

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "error.auth.forbidden");
    }

    [Fact]
    public async Task The_administrator_reaches_every_read_endpoint()
    {
        var (token, _) = await UserApi.AdminAsync(_fixture, _client);

        Assert.Equal(HttpStatusCode.OK, (await UserApi.SendAsync(_client, HttpMethod.Get, "/api/users", token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await UserApi.SendAsync(_client, HttpMethod.Get, "/api/permissions", token)).StatusCode);
    }
}
