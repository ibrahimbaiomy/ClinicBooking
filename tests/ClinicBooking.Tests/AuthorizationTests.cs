using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicBooking.Application.Interfaces;
using ClinicBooking.Domain.Permissions;
using ClinicBooking.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using static ClinicBooking.Tests.Support.AuthHelpers;

namespace ClinicBooking.Tests;

[Collection(SqlServerCollection.Name)]
public class AuthorizationTests : IClassFixture<AuthApiFixture>
{
    private static readonly DateTimeOffset BaseTime = new(2026, 6, 1, 8, 0, 0, TimeSpan.Zero);

    private readonly AuthApiFixture _fixture;
    private readonly HttpClient _client;

    public AuthorizationTests(AuthApiFixture fixture)
    {
        _fixture = fixture;
        _fixture.Clock.Now = BaseTime;
        _client = fixture.Factory.CreateClient();
    }

    [Fact]
    public async Task No_token_is_401_problem_details_with_the_unauthorized_key()
    {
        var response = await _client.GetAsync("/test/protected/authenticated");

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "error.auth.unauthorized");
    }

    [Fact]
    public async Task Garbage_token_is_401()
    {
        var response = await _client.SendAsync(WithBearer(HttpMethod.Get, "/test/protected/authenticated", "not.a.jwt"));

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "error.auth.unauthorized");
    }

    [Fact]
    public async Task Expired_access_token_is_401()
    {
        var user = await CreateUserAsync(_fixture.Factory.Services);
        var token = await AccessTokenAsync(await LoginAsync(_client, user.UserName!, Password));

        _fixture.Clock.Now = BaseTime.AddMinutes(16);
        var response = await _client.SendAsync(WithBearer(HttpMethod.Get, "/test/protected/authenticated", token));

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "error.auth.unauthorized");
    }

    [Fact]
    public async Task Missing_permission_is_403_problem_details_with_the_forbidden_key()
    {
        var user = await CreateUserAsync(_fixture.Factory.Services);
        var token = await AccessTokenAsync(await LoginAsync(_client, user.UserName!, Password));

        var response = await _client.SendAsync(WithBearer(HttpMethod.Get, "/test/protected/users-manage", token));

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "error.auth.forbidden");
    }

    [Fact]
    public async Task Holding_the_permission_is_200()
    {
        var user = await CreateUserAsync(_fixture.Factory.Services, Permissions.Users.Manage);
        var token = await AccessTokenAsync(await LoginAsync(_client, user.UserName!, Password));

        var response = await _client.SendAsync(WithBearer(HttpMethod.Get, "/test/protected/users-manage", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_permission_granted_to_someone_else_does_not_help()
    {
        await CreateUserAsync(_fixture.Factory.Services, Permissions.Users.Manage);
        var other = await CreateUserAsync(_fixture.Factory.Services);
        var token = await AccessTokenAsync(await LoginAsync(_client, other.UserName!, Password));

        var response = await _client.SendAsync(WithBearer(HttpMethod.Get, "/test/protected/users-manage", token));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Me_returns_the_user_and_its_permissions()
    {
        var user = await CreateUserAsync(_fixture.Factory.Services, Permissions.Specialties.Manage, Permissions.Users.Manage);
        var token = await AccessTokenAsync(await LoginAsync(_client, user.UserName!, Password));

        var response = await _client.SendAsync(WithBearer(HttpMethod.Get, "/api/auth/me", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(user.Id, body.GetProperty("id").GetInt64());
        Assert.Equal(user.UserName, body.GetProperty("userName").GetString());
        Assert.Equal(
            [Permissions.Specialties.Manage, Permissions.Users.Manage],
            body.GetProperty("permissions").EnumerateArray().Select(p => p.GetString()!).ToArray());
    }

    [Fact]
    public async Task Me_without_a_token_is_401()
    {
        var response = await _client.GetAsync("/api/auth/me");

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "error.auth.unauthorized");
    }

    [Fact]
    public async Task Audit_fields_record_the_authenticated_user()
    {
        var user = await CreateUserAsync(_fixture.Factory.Services);
        var token = await AccessTokenAsync(await LoginAsync(_client, user.UserName!, Password));

        var response = await _client.SendAsync(WithBearer(HttpMethod.Post, "/test/protected/specialty", token));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var specialty = await db.Specialties.AsNoTracking().SingleAsync(s => s.Id == id);
        Assert.Equal(user.Id, specialty.CreatedBy);
        Assert.Equal(BaseTime, specialty.CreatedAt);
    }
}
