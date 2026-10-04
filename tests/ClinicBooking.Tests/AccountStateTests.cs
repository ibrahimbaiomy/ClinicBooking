using System.Net;
using ClinicBooking.Application.Interfaces;
using ClinicBooking.Domain.Permissions;
using ClinicBooking.Infrastructure.Persistence;
using ClinicBooking.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using static ClinicBooking.Tests.Support.AuthHelpers;

namespace ClinicBooking.Tests;

/// <summary>Disabling and re-enabling users (D57).</summary>
[Collection(SqlServerCollection.Name)]
public class AccountStateTests : IClassFixture<AuthApiFixture>
{
    private readonly AuthApiFixture _fixture;
    private readonly HttpClient _client;

    public AccountStateTests(AuthApiFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Factory.CreateClient();
    }

    [Fact]
    public async Task A_disabled_user_cannot_sign_in_and_the_answer_is_the_one_for_a_wrong_password()
    {
        var (admin, _) = await UserApi.AdminAsync(_fixture, _client);
        var (user, password, _) = await UserApi.ReadyUserAsync(_client, admin);

        var disabled = await UserApi.SendAsync(_client, HttpMethod.Post, $"/api/users/{user.Id}/disable", admin);
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        Assert.False((await ProblemAsync(disabled)).GetProperty("isActive").GetBoolean());

        var withRightPassword = await LoginAsync(_client, user.UserName, password);
        var withWrongPassword = await LoginAsync(_client, user.UserName, "Wrong-Password-123");

        await AssertProblemAsync(withRightPassword, HttpStatusCode.Unauthorized, "error.auth.invalid_credentials");
        Assert.Equal(await UserApi.ComparableAsync(withWrongPassword), await UserApi.ComparableAsync(withRightPassword));
        Assert.Null(RefreshCookie(withRightPassword));
    }

    [Fact]
    public async Task A_disabled_user_looks_the_same_as_an_unknown_one_and_is_not_locked_out_by_trying()
    {
        var (admin, _) = await UserApi.AdminAsync(_fixture, _client);
        var (user, password, _) = await UserApi.ReadyUserAsync(_client, admin);
        await UserApi.SendAsync(_client, HttpMethod.Post, $"/api/users/{user.Id}/disable", admin);

        for (var attempt = 0; attempt < 7; attempt++)
        {
            // Never 423: the lockout is not consulted for a disabled account, so nothing reveals it.
            await AssertProblemAsync(
                await LoginAsync(_client, user.UserName, password), HttpStatusCode.Unauthorized, "error.auth.invalid_credentials");
        }

        var unknown = await LoginAsync(_client, "no_such_" + Guid.NewGuid().ToString("N")[..8], password);
        Assert.Equal(
            await UserApi.ComparableAsync(unknown),
            await UserApi.ComparableAsync(await LoginAsync(_client, user.UserName, password)));
    }

    [Fact]
    public async Task Disabling_revokes_the_refresh_sessions_and_the_cookie_is_cleared()
    {
        var (admin, _) = await UserApi.AdminAsync(_fixture, _client);
        var (user, password, _) = await UserApi.ReadyUserAsync(_client, admin);
        var first = await LoginAsync(_client, user.UserName, password);
        var second = await LoginAsync(_client, user.UserName, password);

        await UserApi.SendAsync(_client, HttpMethod.Post, $"/api/users/{user.Id}/disable", admin);

        var refresh = await PostWithCookieAsync(_client, "/api/auth/refresh", RefreshCookie(first));
        await AssertProblemAsync(refresh, HttpStatusCode.Unauthorized, "error.auth.invalid_refresh_token");
        Assert.StartsWith($"{CookieName}=;", SetCookie(refresh)!);
        await AssertProblemAsync(
            await PostWithCookieAsync(_client, "/api/auth/refresh", RefreshCookie(second)),
            HttpStatusCode.Unauthorized,
            "error.auth.invalid_refresh_token");

        // Re-enabling does not bring the old sessions back.
        await UserApi.SendAsync(_client, HttpMethod.Post, $"/api/users/{user.Id}/enable", admin);
        await AssertProblemAsync(
            await PostWithCookieAsync(_client, "/api/auth/refresh", RefreshCookie(first)),
            HttpStatusCode.Unauthorized,
            "error.auth.invalid_refresh_token");
    }

    [Theory]
    [InlineData("GET", "/api/auth/me")]
    [InlineData("GET", "/api/specialties")]
    [InlineData("GET", "/test/protected/authenticated")]
    [InlineData("POST", "/api/auth/change-password")]
    public async Task An_access_token_issued_before_the_user_was_disabled_stops_working_at_the_next_request(string method, string url)
    {
        var (admin, _) = await UserApi.AdminAsync(_fixture, _client);
        var (user, _, token) = await UserApi.ReadyUserAsync(_client, admin);
        Assert.Equal(HttpStatusCode.OK, (await UserApi.SendAsync(_client, HttpMethod.Get, "/api/auth/me", token)).StatusCode);

        await UserApi.SendAsync(_client, HttpMethod.Post, $"/api/users/{user.Id}/disable", admin);

        // The token has not expired (the clock did not move): the gate cuts it off, not the lifetime.
        var response = await UserApi.SendAsync(_client, new HttpMethod(method), url, token, method == "POST" ? new { } : null);
        await AssertProblemAsync(response, HttpStatusCode.Unauthorized, "error.auth.unauthorized");
    }

    [Fact]
    public async Task A_disabled_users_permissions_are_kept_but_grant_nothing_until_the_user_is_enabled_again()
    {
        var (admin, _) = await UserApi.AdminAsync(_fixture, _client);
        var (user, password, _) = await UserApi.ReadyUserAsync(_client, admin);
        await UserApi.SendAsync(
            _client, HttpMethod.Put, $"/api/users/{user.Id}/global-permissions", admin,
            new { permissions = new[] { Permissions.Clinics.Manage } });

        await UserApi.SendAsync(_client, HttpMethod.Post, $"/api/users/{user.Id}/disable", admin);

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var checker = scope.ServiceProvider.GetRequiredService<IPermissionChecker>();
            Assert.False(await checker.HasGlobalPermissionAsync(user.Id, Permissions.Clinics.Manage, CancellationToken.None));
            // What is stored is still listed, so an administrator sees it.
            Assert.Equal([Permissions.Clinics.Manage], await checker.GetGlobalPermissionsAsync(user.Id, CancellationToken.None));
        }

        var detail = await UserApi.DetailAsync(_client, admin, user.Id);
        Assert.Equal([Permissions.Clinics.Manage], UserApi.Strings(detail.GetProperty("globalPermissions")));

        await UserApi.SendAsync(_client, HttpMethod.Post, $"/api/users/{user.Id}/enable", admin);

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var checker = scope.ServiceProvider.GetRequiredService<IPermissionChecker>();
            Assert.True(await checker.HasGlobalPermissionAsync(user.Id, Permissions.Clinics.Manage, CancellationToken.None));
        }

        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(_client, user.UserName, password)).StatusCode);
    }

    [Fact]
    public async Task Re_enabling_lets_the_user_sign_in_again_and_both_calls_are_idempotent()
    {
        var (admin, _) = await UserApi.AdminAsync(_fixture, _client);
        var (user, password, _) = await UserApi.ReadyUserAsync(_client, admin);

        Assert.Equal(HttpStatusCode.OK, (await UserApi.SendAsync(_client, HttpMethod.Post, $"/api/users/{user.Id}/enable", admin)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await UserApi.SendAsync(_client, HttpMethod.Post, $"/api/users/{user.Id}/disable", admin)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await UserApi.SendAsync(_client, HttpMethod.Post, $"/api/users/{user.Id}/disable", admin)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(_client, user.UserName, password)).StatusCode);

        var enabled = await UserApi.SendAsync(_client, HttpMethod.Post, $"/api/users/{user.Id}/enable", admin);
        Assert.True((await ProblemAsync(enabled)).GetProperty("isActive").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(_client, user.UserName, password)).StatusCode);
    }

    [Fact]
    public async Task Disabling_and_enabling_an_unknown_user_is_404()
    {
        var (admin, _) = await UserApi.AdminAsync(_fixture, _client);

        await AssertProblemAsync(
            await UserApi.SendAsync(_client, HttpMethod.Post, "/api/users/999999999/disable", admin),
            HttpStatusCode.NotFound,
            "error.user.not_found");
        await AssertProblemAsync(
            await UserApi.SendAsync(_client, HttpMethod.Post, "/api/users/999999999/enable", admin),
            HttpStatusCode.NotFound,
            "error.user.not_found");
    }

    [Fact]
    public async Task Disabling_stamps_the_audit_fields()
    {
        _fixture.Clock.Now = new DateTimeOffset(2026, 5, 6, 7, 8, 9, TimeSpan.Zero);
        var (admin, adminId) = await UserApi.AdminAsync(_fixture, _client);
        var user = await UserApi.CreateUserAsync(_client, admin);
        _fixture.Clock.Now = _fixture.Clock.Now.AddMinutes(1); // inside the 15-minute token lifetime

        var disabled = await UserApi.SendAsync(_client, HttpMethod.Post, $"/api/users/{user.Id}/disable", admin);
        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.Users.SingleAsync(u => u.Id == user.Id);
        Assert.Equal(adminId, stored.UpdatedBy);
        Assert.Equal(_fixture.Clock.Now, stored.UpdatedAt);
    }
}
