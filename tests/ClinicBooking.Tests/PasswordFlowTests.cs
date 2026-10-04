using System.Net;
using System.Net.Http.Json;
using ClinicBooking.Domain.Permissions;
using ClinicBooking.Tests.Support;
using static ClinicBooking.Tests.Support.AuthHelpers;
using static ClinicBooking.Tests.Support.SpecialtyHelpers;

namespace ClinicBooking.Tests;

/// <summary>MustChangePassword enforcement, change-password and admin reset-password (D57).</summary>
[Collection(SqlServerCollection.Name)]
public class PasswordFlowTests : IClassFixture<AuthApiFixture>
{
    private const string NewPassword = "Brand-New-Password-42";

    private readonly AuthApiFixture _fixture;
    private readonly HttpClient _client;

    public PasswordFlowTests(AuthApiFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Factory.CreateClient();
    }

    private async Task<string> AdminTokenAsync() => (await UserApi.AdminAsync(_fixture, _client)).Token;

    // A user who has just been created and signed in with the temporary password.
    private async Task<(UserApi.Created User, string Token, string Cookie)> TemporaryUserAsync(string admin)
    {
        var user = await UserApi.CreateUserAsync(_client, admin);
        var login = await LoginAsync(_client, user.UserName, user.Password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        return (user, await AccessTokenAsync(login), RefreshCookie(login)!);
    }

    // --- MustChangePassword enforcement ---

    [Theory]
    [InlineData("GET", "/api/specialties")]
    [InlineData("GET", "/api/clinics")]
    [InlineData("GET", "/api/users")]
    [InlineData("GET", "/api/permissions")]
    [InlineData("POST", "/api/specialties")]
    public async Task While_the_password_is_temporary_every_other_endpoint_is_403_password_change_required(string method, string url)
    {
        var admin = await AdminTokenAsync();
        var (user, token, _) = await TemporaryUserAsync(admin);
        // Even a user with the permission is blocked: the gate runs before authorization.
        await UserApi.SendAsync(
            _client, HttpMethod.Put, $"/api/users/{user.Id}/global-permissions", admin,
            new { permissions = Permissions.Global.ToArray() });

        var response = await UserApi.SendAsync(_client, new HttpMethod(method), url, token, method == "POST" ? new { } : null);

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "error.auth.password_change_required");
    }

    [Fact]
    public async Task While_the_password_is_temporary_me_and_change_password_work_and_me_says_so()
    {
        var admin = await AdminTokenAsync();
        var (user, token, cookie) = await TemporaryUserAsync(admin);

        var me = await UserApi.SendAsync(_client, HttpMethod.Get, "/api/auth/me", token);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.True((await ProblemAsync(me)).GetProperty("mustChangePassword").GetBoolean());

        var changed = await UserApi.ChangePasswordAsync(_client, token, cookie, user.Password, NewPassword);
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
    }

    [Fact]
    public async Task While_the_password_is_temporary_refresh_and_logout_still_work()
    {
        var admin = await AdminTokenAsync();
        var (_, _, cookie) = await TemporaryUserAsync(admin);

        var refresh = await PostWithCookieAsync(_client, "/api/auth/refresh", cookie);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);

        var logout = await PostWithCookieAsync(_client, "/api/auth/logout", RefreshCookie(refresh));
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
    }

    [Fact]
    public async Task Changing_the_password_clears_the_flag_and_the_same_token_then_works_everywhere()
    {
        var admin = await AdminTokenAsync();
        var (user, token, cookie) = await TemporaryUserAsync(admin);

        var changed = await UserApi.ChangePasswordAsync(_client, token, cookie, user.Password, NewPassword);
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

        // Read from the database on every request: no new token is needed.
        Assert.Equal(HttpStatusCode.OK, (await UserApi.SendAsync(_client, HttpMethod.Get, "/api/specialties", token)).StatusCode);
        var me = await ProblemAsync(await UserApi.SendAsync(_client, HttpMethod.Get, "/api/auth/me", token));
        Assert.False(me.GetProperty("mustChangePassword").GetBoolean());

        // The old password is gone, the new one works.
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginAsync(_client, user.UserName, user.Password)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(_client, user.UserName, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task The_flag_enforcement_is_not_a_policy_a_user_without_any_permission_is_blocked_too()
    {
        var admin = await AdminTokenAsync();
        var (_, token, _) = await TemporaryUserAsync(admin);

        // /api/auth/me is the only authenticated endpoint a [Authorize]-only caller may use: others are blocked.
        var response = await UserApi.SendAsync(_client, HttpMethod.Get, "/test/protected/authenticated", token);

        await AssertProblemAsync(response, HttpStatusCode.Forbidden, "error.auth.password_change_required");
    }

    // --- change-password ---

    [Fact]
    public async Task A_wrong_current_password_is_a_400_with_its_key_on_the_field()
    {
        var admin = await AdminTokenAsync();
        var (user, token, cookie) = await TemporaryUserAsync(admin);

        var response = await UserApi.ChangePasswordAsync(_client, token, cookie, "Wrong-Password-123", NewPassword);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "error.auth.current_password_incorrect");
        Assert.Equal(
            "error.auth.current_password_incorrect",
            (await ProblemAsync(response)).GetProperty("errors").GetProperty("currentPassword")[0].GetString());
        // Nothing changed.
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(_client, user.UserName, user.Password)).StatusCode);
    }

    [Theory]
    [InlineData("Ab1", "error.password.too_short")]
    [InlineData("nouppercase-1234567", "error.password.requires_uppercase")]
    [InlineData("NOLOWERCASE-1234567", "error.password.requires_lowercase")]
    [InlineData("No-Digits-Anywhere-Here", "error.password.requires_digit")]
    [InlineData("Aa1Aa1Aa1Aa1Aa1Aa1", "error.password.requires_unique_chars")]
    public async Task A_weak_new_password_is_a_400_with_the_policy_key_on_new_password(string weak, string key)
    {
        var admin = await AdminTokenAsync();
        var (user, token, cookie) = await TemporaryUserAsync(admin);

        var response = await UserApi.ChangePasswordAsync(_client, token, cookie, user.Password, weak);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "error.validation.failed");
        var keys = UserApi.Strings((await ProblemAsync(response)).GetProperty("errors").GetProperty("newPassword"));
        Assert.Contains(key, keys);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(_client, user.UserName, user.Password)).StatusCode); // unchanged
    }

    [Fact]
    public async Task The_new_password_must_differ_from_the_current_one()
    {
        var admin = await AdminTokenAsync();
        var (user, token, cookie) = await TemporaryUserAsync(admin);

        var response = await UserApi.ChangePasswordAsync(_client, token, cookie, user.Password, user.Password);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "error.auth.password_unchanged");
        Assert.Equal(
            "error.auth.password_unchanged",
            (await ProblemAsync(response)).GetProperty("errors").GetProperty("newPassword")[0].GetString());
    }

    [Theory]
    [InlineData(null, NewPassword, "currentPassword", "error.auth.current_password_required")]
    [InlineData("", NewPassword, "currentPassword", "error.auth.current_password_required")]
    [InlineData("Some-Current-Password-1", null, "newPassword", "error.password.required")]
    [InlineData("Some-Current-Password-1", "", "newPassword", "error.password.required")]
    public async Task Both_passwords_are_required(string? current, string? next, string field, string key)
    {
        var admin = await AdminTokenAsync();
        var (_, token, cookie) = await TemporaryUserAsync(admin);

        var response = await UserApi.ChangePasswordAsync(_client, token, cookie, current, next);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "error.validation.failed");
        Assert.Equal(key, (await ProblemAsync(response)).GetProperty("errors").GetProperty(field)[0].GetString());
    }

    [Fact]
    public async Task Change_password_needs_a_token_and_the_same_origin()
    {
        var admin = await AdminTokenAsync();
        var (user, token, cookie) = await TemporaryUserAsync(admin);

        await AssertProblemAsync(
            await UserApi.ChangePasswordAsync(_client, null, cookie, user.Password, NewPassword),
            HttpStatusCode.Unauthorized,
            "error.auth.unauthorized");
        await AssertProblemAsync(
            await UserApi.ChangePasswordAsync(_client, token, cookie, user.Password, NewPassword, origin: "https://evil.example"),
            HttpStatusCode.Forbidden,
            "error.auth.forbidden");
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(_client, user.UserName, user.Password)).StatusCode); // unchanged
    }

    [Fact]
    public async Task Repeated_wrong_current_passwords_lock_the_account()
    {
        var admin = await AdminTokenAsync();
        var (user, token, cookie) = await TemporaryUserAsync(admin);

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var wrong = await UserApi.ChangePasswordAsync(_client, token, cookie, $"Wrong-Password-{attempt}x", NewPassword);
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        }

        // The fifth failure locked the account: even the right current password is refused now, and so is login.
        var locked = await UserApi.ChangePasswordAsync(_client, token, cookie, user.Password, NewPassword);
        await AssertProblemAsync(locked, HttpStatusCode.Locked, "error.auth.locked_out");
        await AssertProblemAsync(await LoginAsync(_client, user.UserName, user.Password), HttpStatusCode.Locked, "error.auth.locked_out");
    }

    [Fact]
    public async Task A_correct_current_password_resets_the_failure_counter()
    {
        var admin = await AdminTokenAsync();
        var (user, token, cookie) = await TemporaryUserAsync(admin);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            await UserApi.ChangePasswordAsync(_client, token, cookie, $"Wrong-Password-{attempt}x", NewPassword);
        }

        // The success resets the counter (4 failures would be 8 with the next four if it did not).
        var changed = await UserApi.ChangePasswordAsync(_client, token, cookie, user.Password, NewPassword);
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            await UserApi.ChangePasswordAsync(_client, token, cookie, $"Other-Wrong-{attempt}x1", "Yet-Another-Password-7");
        }

        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(_client, user.UserName, NewPassword)).StatusCode);
    }

    // --- sessions ---

    [Fact]
    public async Task Change_password_revokes_the_other_sessions_and_keeps_the_current_one()
    {
        var admin = await AdminTokenAsync();
        var user = await UserApi.CreateUserAsync(_client, admin);
        var current = await LoginAsync(_client, user.UserName, user.Password);
        var other1 = await LoginAsync(_client, user.UserName, user.Password);
        var other2 = await LoginAsync(_client, user.UserName, user.Password);

        var changed = await UserApi.ChangePasswordAsync(
            _client, await AccessTokenAsync(current), RefreshCookie(current), user.Password, NewPassword);
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await PostWithCookieAsync(_client, "/api/auth/refresh", RefreshCookie(current))).StatusCode);
        await AssertProblemAsync(
            await PostWithCookieAsync(_client, "/api/auth/refresh", RefreshCookie(other1)),
            HttpStatusCode.Unauthorized,
            "error.auth.invalid_refresh_token");
        await AssertProblemAsync(
            await PostWithCookieAsync(_client, "/api/auth/refresh", RefreshCookie(other2)),
            HttpStatusCode.Unauthorized,
            "error.auth.invalid_refresh_token");
    }

    [Fact]
    public async Task Without_the_cookie_every_session_is_revoked_but_the_access_token_lives_on()
    {
        var admin = await AdminTokenAsync();
        var user = await UserApi.CreateUserAsync(_client, admin);
        var first = await LoginAsync(_client, user.UserName, user.Password);
        var second = await LoginAsync(_client, user.UserName, user.Password);
        var token = await AccessTokenAsync(first);

        var changed = await UserApi.ChangePasswordAsync(_client, token, cookie: null, user.Password, NewPassword);
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

        await AssertProblemAsync(
            await PostWithCookieAsync(_client, "/api/auth/refresh", RefreshCookie(first)),
            HttpStatusCode.Unauthorized,
            "error.auth.invalid_refresh_token");
        await AssertProblemAsync(
            await PostWithCookieAsync(_client, "/api/auth/refresh", RefreshCookie(second)),
            HttpStatusCode.Unauthorized,
            "error.auth.invalid_refresh_token");
        Assert.Equal(HttpStatusCode.OK, (await UserApi.SendAsync(_client, HttpMethod.Get, "/api/specialties", token)).StatusCode);
    }

    [Fact]
    public async Task Another_users_cookie_cannot_name_the_current_session_so_every_session_is_revoked()
    {
        var admin = await AdminTokenAsync();
        var user = await UserApi.CreateUserAsync(_client, admin);
        var stranger = await UserApi.CreateUserAsync(_client, admin);
        var mine = await LoginAsync(_client, user.UserName, user.Password);
        var theirs = await LoginAsync(_client, stranger.UserName, stranger.Password);

        var changed = await UserApi.ChangePasswordAsync(
            _client, await AccessTokenAsync(mine), RefreshCookie(theirs), user.Password, NewPassword);
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

        await AssertProblemAsync(
            await PostWithCookieAsync(_client, "/api/auth/refresh", RefreshCookie(mine)),
            HttpStatusCode.Unauthorized,
            "error.auth.invalid_refresh_token");
        // The stranger's session is untouched.
        Assert.Equal(HttpStatusCode.OK, (await PostWithCookieAsync(_client, "/api/auth/refresh", RefreshCookie(theirs))).StatusCode);
    }

    [Fact]
    public async Task A_revoked_cookie_cannot_name_the_current_session()
    {
        var admin = await AdminTokenAsync();
        var user = await UserApi.CreateUserAsync(_client, admin);
        var session = await LoginAsync(_client, user.UserName, user.Password);
        var other = await LoginAsync(_client, user.UserName, user.Password);
        await PostWithCookieAsync(_client, "/api/auth/logout", RefreshCookie(session)); // revokes this session

        var changed = await UserApi.ChangePasswordAsync(
            _client, await AccessTokenAsync(other), RefreshCookie(session), user.Password, NewPassword);
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

        await AssertProblemAsync(
            await PostWithCookieAsync(_client, "/api/auth/refresh", RefreshCookie(other)),
            HttpStatusCode.Unauthorized,
            "error.auth.invalid_refresh_token");
    }

    // --- admin reset-password ---

    [Fact]
    public async Task Reset_sets_a_new_temporary_password_ends_old_sessions_and_forces_a_change()
    {
        var (admin, _) = await UserApi.AdminAsync(_fixture, _client);
        var (user, oldPassword, oldToken) = await UserApi.ReadyUserAsync(_client, admin);
        var oldSession = await LoginAsync(_client, user.UserName, oldPassword);
        var temporary = UserApi.NewTemporaryPassword();

        var reset = await UserApi.SendAsync(
            _client, HttpMethod.Post, $"/api/users/{user.Id}/reset-password", admin, new { temporaryPassword = temporary });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.Equal(0, reset.Content.Headers.ContentLength ?? 0);

        // The old password and the old sessions stop working.
        await AssertProblemAsync(
            await LoginAsync(_client, user.UserName, oldPassword), HttpStatusCode.Unauthorized, "error.auth.invalid_credentials");
        await AssertProblemAsync(
            await PostWithCookieAsync(_client, "/api/auth/refresh", RefreshCookie(oldSession)),
            HttpStatusCode.Unauthorized,
            "error.auth.invalid_refresh_token");

        // The old access token is now blocked like any session of a user with a temporary password.
        await AssertProblemAsync(
            await UserApi.SendAsync(_client, HttpMethod.Get, "/api/specialties", oldToken),
            HttpStatusCode.Forbidden,
            "error.auth.password_change_required");

        // The new temporary password works, and a change is enforced again.
        var login = await LoginAsync(_client, user.UserName, temporary);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = await AccessTokenAsync(login);
        await AssertProblemAsync(
            await UserApi.SendAsync(_client, HttpMethod.Get, "/api/specialties", token),
            HttpStatusCode.Forbidden,
            "error.auth.password_change_required");
        Assert.True((await UserApi.DetailAsync(_client, admin, user.Id)).GetProperty("mustChangePassword").GetBoolean());

        var changed = await UserApi.ChangePasswordAsync(_client, token, RefreshCookie(login), temporary, NewPassword);
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await UserApi.SendAsync(_client, HttpMethod.Get, "/api/specialties", token)).StatusCode);
    }

    [Fact]
    public async Task Reset_clears_a_lockout()
    {
        var (admin, _) = await UserApi.AdminAsync(_fixture, _client);
        var user = await UserApi.CreateUserAsync(_client, admin);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await LoginAsync(_client, user.UserName, "Wrong-Password-123");
        }

        await AssertProblemAsync(await LoginAsync(_client, user.UserName, user.Password), HttpStatusCode.Locked, "error.auth.locked_out");

        var temporary = UserApi.NewTemporaryPassword();
        var reset = await UserApi.SendAsync(
            _client, HttpMethod.Post, $"/api/users/{user.Id}/reset-password", admin, new { temporaryPassword = temporary });
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(_client, user.UserName, temporary)).StatusCode);
    }

    [Fact]
    public async Task Reset_leaves_a_disabled_user_disabled()
    {
        var (admin, _) = await UserApi.AdminAsync(_fixture, _client);
        var user = await UserApi.CreateUserAsync(_client, admin);
        await UserApi.SendAsync(_client, HttpMethod.Post, $"/api/users/{user.Id}/disable", admin);

        var temporary = UserApi.NewTemporaryPassword();
        var reset = await UserApi.SendAsync(
            _client, HttpMethod.Post, $"/api/users/{user.Id}/reset-password", admin, new { temporaryPassword = temporary });

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        Assert.False((await UserApi.DetailAsync(_client, admin, user.Id)).GetProperty("isActive").GetBoolean());
        await AssertProblemAsync(
            await LoginAsync(_client, user.UserName, temporary), HttpStatusCode.Unauthorized, "error.auth.invalid_credentials");
    }

    [Fact]
    public async Task Reset_refuses_the_callers_own_account()
    {
        var (admin, adminId) = await UserApi.AdminAsync(_fixture, _client);

        var response = await UserApi.SendAsync(
            _client, HttpMethod.Post, $"/api/users/{adminId}/reset-password", admin,
            new { temporaryPassword = UserApi.NewTemporaryPassword() });

        await AssertProblemAsync(response, HttpStatusCode.UnprocessableEntity, "error.user.cannot_reset_own_password");
    }

    [Fact]
    public async Task Reset_of_an_unknown_user_is_404()
    {
        var (admin, _) = await UserApi.AdminAsync(_fixture, _client);

        var response = await UserApi.SendAsync(
            _client, HttpMethod.Post, "/api/users/999999999/reset-password", admin,
            new { temporaryPassword = UserApi.NewTemporaryPassword() });

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "error.user.not_found");
    }

    [Theory]
    [InlineData("", "error.password.required")]
    [InlineData("Ab1", "error.password.too_short")]
    [InlineData("nouppercase-1234567", "error.password.requires_uppercase")]
    [InlineData("Aa1Aa1Aa1Aa1Aa1Aa1", "error.password.requires_unique_chars")]
    public async Task Reset_applies_the_password_policy_and_changes_nothing_when_it_fails(string weak, string key)
    {
        var (admin, _) = await UserApi.AdminAsync(_fixture, _client);
        var user = await UserApi.CreateUserAsync(_client, admin);

        var response = await UserApi.SendAsync(
            _client, HttpMethod.Post, $"/api/users/{user.Id}/reset-password", admin, new { temporaryPassword = weak });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "error.validation.failed");
        Assert.Contains(
            key,
            UserApi.Strings((await ProblemAsync(response)).GetProperty("errors").GetProperty("temporaryPassword")));
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(_client, user.UserName, user.Password)).StatusCode);
    }
}

/// <summary>The change-password limiter is its own policy, partitioned by user (D57).</summary>
[Collection(SqlServerCollection.Name)]
public class ChangePasswordRateLimitTests : IClassFixture<LowChangePasswordLimitAuthFixture>
{
    private readonly LowChangePasswordLimitAuthFixture _fixture;
    private readonly HttpClient _client;

    public ChangePasswordRateLimitTests(LowChangePasswordLimitAuthFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Factory.CreateClient();
    }

    [Fact]
    public async Task The_limit_is_per_user_and_does_not_touch_login_or_other_users()
    {
        var (admin, _) = await UserApi.AdminAsync(_fixture, _client);
        var first = await UserApi.CreateUserAsync(_client, admin);
        var second = await UserApi.CreateUserAsync(_client, admin);
        var firstLogin = await LoginAsync(_client, first.UserName, first.Password);
        var secondLogin = await LoginAsync(_client, second.UserName, second.Password);
        var firstToken = await AccessTokenAsync(firstLogin);
        var secondToken = await AccessTokenAsync(secondLogin);

        // Three wrong attempts are allowed (400), the fourth is limited (429, with Retry-After).
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var wrong = await UserApi.ChangePasswordAsync(_client, firstToken, RefreshCookie(firstLogin), "Wrong-Password-1x", "Whatever-Pass-123");
            Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        }

        var limited = await UserApi.ChangePasswordAsync(_client, firstToken, RefreshCookie(firstLogin), first.Password, "Whatever-Pass-123");
        await AssertProblemAsync(limited, HttpStatusCode.TooManyRequests, "error.auth.rate_limited");
        Assert.True(limited.Headers.Contains("Retry-After"));

        // Same client address, another user: not limited. Login: not limited either.
        var other = await UserApi.ChangePasswordAsync(_client, secondToken, RefreshCookie(secondLogin), second.Password, "Whatever-Pass-123");
        Assert.Equal(HttpStatusCode.NoContent, other.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await LoginAsync(_client, first.UserName, first.Password)).StatusCode);
    }
}
