using System.Net;
using ClinicBooking.Domain.Permissions;
using ClinicBooking.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using static ClinicBooking.Tests.Support.AuthHelpers;
using static ClinicBooking.Tests.Support.SpecialtyHelpers;

namespace ClinicBooking.Tests;

[Collection(SqlServerCollection.Name)]
public class ClinicsAuthorizationTests : IClassFixture<AuthApiFixture>
{
    private readonly AuthApiFixture _fixture;
    private readonly HttpClient _client;

    public ClinicsAuthorizationTests(AuthApiFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Factory.CreateClient();
    }

    public static TheoryData<string, string> Endpoints => new()
    {
        { "GET", "/api/clinics" },
        { "GET", "/api/clinics/1" },
        { "POST", "/api/clinics" },
        { "PUT", "/api/clinics/1" },
        { "DELETE", "/api/clinics/1" }
    };

    public static TheoryData<string, string> ChangingEndpoints => new()
    {
        { "POST", "/api/clinics" },
        { "PUT", "/api/clinics/1" },
        { "DELETE", "/api/clinics/1" }
    };

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Every_endpoint_is_401_without_a_token(string method, string url)
    {
        var response = await _client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "error.auth.unauthorized");
    }

    [Theory]
    [MemberData(nameof(ChangingEndpoints))]
    public async Task Changing_endpoints_are_403_without_the_manage_permission(string method, string url)
    {
        var (token, _) = await SignInAsync(_fixture, _client); // signed in, no permissions

        var response = await _client.SendAsync(Json(new HttpMethod(method), url, token, new { nameAr = "ا", nameEn = "a" }));

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "error.auth.forbidden");
    }

    [Theory]
    [MemberData(nameof(ChangingEndpoints))]
    public async Task Another_permission_does_not_unlock_clinics(string method, string url)
    {
        var (token, _) = await SignInAsync(_fixture, _client, Permissions.Specialties.Manage, Permissions.Users.Manage);

        var response = await _client.SendAsync(Json(new HttpMethod(method), url, token, new { nameAr = "ا", nameEn = "a" }));

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "error.auth.forbidden");
    }

    [Fact]
    public async Task Reading_needs_only_a_signed_in_user()
    {
        var (token, _) = await SignInAsync(_fixture, _client); // no permissions
        var manager = await ClinicHelpers.ClinicManagerTokenAsync(_fixture, _client);
        var created = await ClinicHelpers.CreateClinicOkAsync(_client, manager, UniqueArabic(), UniqueEnglish());

        var list = await _client.SendAsync(Json(HttpMethod.Get, "/api/clinics", token));
        var one = await _client.SendAsync(Json(HttpMethod.Get, $"/api/clinics/{created.GetProperty("id").GetInt64()}", token));

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.OK, one.StatusCode);
    }

    [Fact]
    public async Task A_revoked_permission_applies_at_once()
    {
        // Permissions are read from the database on every call (D34), not from the token.
        var (token, userId) = await SignInAsync(_fixture, _client, Permissions.Clinics.Manage);
        var created = await ClinicHelpers.CreateClinicAsync(_client, token, UniqueArabic(), UniqueEnglish());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        await RemoveClaimAsync(userId, Permissions.Clinics.Manage);
        var after = await ClinicHelpers.CreateClinicAsync(_client, token, UniqueArabic(), UniqueEnglish());

        await AssertProblemAsync(after, HttpStatusCode.Forbidden, "error.auth.forbidden");
    }

    [Fact]
    public async Task An_unknown_route_is_404_for_a_signed_in_user()
    {
        var (token, _) = await SignInAsync(_fixture, _client);

        var response = await _client.SendAsync(Json(HttpMethod.Get, "/api/clinics/abc/extra", token));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task RemoveClaimAsync(long userId, string permission)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ClinicBooking.Infrastructure.Identity.ApplicationUser>>();
        var user = (await users.FindByIdAsync(userId.ToString()))!;
        await users.RemoveClaimAsync(user, new System.Security.Claims.Claim(ClinicBooking.Infrastructure.Identity.PermissionChecker.ClaimType, permission));
    }
}
