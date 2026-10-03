using System.Net;
using ClinicBooking.Tests.Support;
using static ClinicBooking.Tests.Support.AuthHelpers;
using static ClinicBooking.Tests.Support.SpecialtyHelpers;

namespace ClinicBooking.Tests;

[Collection(SqlServerCollection.Name)]
public class SpecialtiesAuthorizationTests : IClassFixture<AuthApiFixture>
{
    private readonly AuthApiFixture _fixture;
    private readonly HttpClient _client;

    public SpecialtiesAuthorizationTests(AuthApiFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Factory.CreateClient();
    }

    public static TheoryData<string, string> Endpoints => new()
    {
        { "GET", "/api/specialties" },
        { "GET", "/api/specialties/1" },
        { "POST", "/api/specialties" },
        { "PUT", "/api/specialties/1" },
        { "DELETE", "/api/specialties/1" }
    };

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Every_endpoint_is_401_without_a_token(string method, string url)
    {
        var response = await _client.SendAsync(Json(new HttpMethod(method), url, token: null, body: new { nameAr = "x", nameEn = "y", rowVersion = "AAAAAAAAAAA=" }));

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "error.auth.unauthorized");
    }

    [Theory]
    [InlineData("POST", "/api/specialties")]
    [InlineData("PUT", "/api/specialties/1")]
    [InlineData("DELETE", "/api/specialties/1")]
    public async Task Changing_endpoints_are_403_without_the_manage_permission(string method, string url)
    {
        var (token, _) = await SignInAsync(_fixture, _client);

        var response = await _client.SendAsync(Json(new HttpMethod(method), url, token, new { nameAr = "x", nameEn = "y", rowVersion = "AAAAAAAAAAA=" }));

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "error.auth.forbidden");
    }

    [Fact]
    public async Task Reading_needs_only_a_signed_in_user()
    {
        var manager = await ManagerTokenAsync(_fixture, _client);
        var created = await CreateOkAsync(_client, manager, UniqueArabic(), UniqueEnglish());
        var (reader, _) = await SignInAsync(_fixture, _client);

        var list = await _client.SendAsync(Json(HttpMethod.Get, "/api/specialties", reader));
        var one = await _client.SendAsync(Json(HttpMethod.Get, $"/api/specialties/{created.GetProperty("id").GetInt64()}", reader));

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(HttpStatusCode.OK, one.StatusCode);
    }

    [Fact]
    public async Task An_unknown_route_is_404_for_a_signed_in_user()
    {
        var (token, _) = await SignInAsync(_fixture, _client);

        var response = await _client.SendAsync(Json(HttpMethod.Get, "/api/does-not-exist", token));

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "error.http.404");
    }
}
