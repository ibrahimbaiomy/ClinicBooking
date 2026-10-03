using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClinicBooking.Application.Interfaces;
using ClinicBooking.Infrastructure.Identity;
using ClinicBooking.Tests.Support;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using static ClinicBooking.Tests.Support.AuthHelpers;

namespace ClinicBooking.Tests;

[Collection(SqlServerCollection.Name)]
public class AuthTests : IClassFixture<AuthApiFixture>
{
    private static readonly DateTimeOffset BaseTime = new(2026, 5, 1, 8, 0, 0, TimeSpan.Zero);

    private readonly AuthApiFixture _fixture;
    private readonly HttpClient _client;

    public AuthTests(AuthApiFixture fixture)
    {
        _fixture = fixture;
        _fixture.Clock.Now = BaseTime;
        _client = fixture.Factory.CreateClient();
    }

    [Fact]
    public async Task Login_succeeds_and_sets_a_hardened_refresh_cookie()
    {
        var user = await CreateUserAsync(_fixture.Factory.Services);

        var response = await LoginAsync(_client, user.UserName!, Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await ProblemAsync(response);
        Assert.False(string.IsNullOrEmpty(body.GetProperty("accessToken").GetString()));
        Assert.Equal(BaseTime.AddMinutes(15), body.GetProperty("expiresAt").GetDateTimeOffset());
        Assert.False(body.TryGetProperty("refreshToken", out _));

        var cookie = SetCookie(response)!;
        Assert.NotNull(RefreshCookie(response));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/api/auth", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);

        // The access token works on a protected endpoint.
        var token = await AccessTokenAsync(response);
        var protectedResponse = await _client.SendAsync(WithBearer(HttpMethod.Get, "/test/protected/authenticated", token));
        Assert.Equal(HttpStatusCode.OK, protectedResponse.StatusCode);
    }

    [Fact]
    public async Task Wrong_password_and_unknown_user_give_the_same_response()
    {
        var user = await CreateUserAsync(_fixture.Factory.Services);

        var wrongPassword = await LoginAsync(_client, user.UserName!, "Wrong-Password-1234");
        var unknownUser = await LoginAsync(_client, $"nobody_{Guid.NewGuid():N}", "Wrong-Password-1234");

        await AssertProblemAsync(wrongPassword, HttpStatusCode.Unauthorized, "error.auth.invalid_credentials");
        await AssertProblemAsync(unknownUser, HttpStatusCode.Unauthorized, "error.auth.invalid_credentials");

        var a = await ProblemAsync(wrongPassword);
        var b = await ProblemAsync(unknownUser);
        Assert.Equal(a.GetProperty("status").GetInt32(), b.GetProperty("status").GetInt32());
        Assert.Equal(a.GetProperty("type").GetString(), b.GetProperty("type").GetString());
        Assert.Equal(a.EnumerateObject().Select(p => p.Name).Order(), b.EnumerateObject().Select(p => p.Name).Order());
        Assert.Null(SetCookie(wrongPassword));
        Assert.Null(SetCookie(unknownUser));
    }

    [Fact]
    public async Task Empty_credentials_are_a_400_with_keys()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new { userName = "", password = "" });

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "error.validation.failed");
    }

    [Fact]
    public async Task Account_locks_after_repeated_failures_whatever_the_password()
    {
        // Identity's lockout uses the system clock; keep the test clock aligned with it.
        _fixture.Clock.Now = DateTimeOffset.UtcNow;
        var user = await CreateUserAsync(_fixture.Factory.Services);

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var failed = await LoginAsync(_client, user.UserName!, "Wrong-Password-1234");
            await AssertProblemAsync(failed, HttpStatusCode.Unauthorized, "error.auth.invalid_credentials");
        }

        // The lockout is checked before the password: the right password changes nothing.
        var withCorrect = await LoginAsync(_client, user.UserName!, Password);
        var withWrong = await LoginAsync(_client, user.UserName!, "Wrong-Password-1234");

        await AssertProblemAsync(withCorrect, HttpStatusCode.Locked, "error.auth.locked_out");
        await AssertProblemAsync(withWrong, HttpStatusCode.Locked, "error.auth.locked_out");
        Assert.True(withCorrect.Headers.TryGetValues("Retry-After", out var retryAfter));
        Assert.InRange(int.Parse(retryAfter!.Single()), 1, 15 * 60 + 5);
        Assert.Null(SetCookie(withCorrect));
    }

    [Fact]
    public async Task Refresh_rotates_the_token()
    {
        var user = await CreateUserAsync(_fixture.Factory.Services);
        var login = await LoginAsync(_client, user.UserName!, Password);
        var first = RefreshCookie(login)!;

        _fixture.Clock.Now = BaseTime.AddMinutes(5);
        var refreshed = await PostWithCookieAsync(_client, "/api/auth/refresh", first);

        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        var second = RefreshCookie(refreshed)!;
        Assert.NotEqual(first, second);
        var token = await AccessTokenAsync(refreshed);
        var protectedResponse = await _client.SendAsync(WithBearer(HttpMethod.Get, "/test/protected/authenticated", token));
        Assert.Equal(HttpStatusCode.OK, protectedResponse.StatusCode);
    }

    [Fact]
    public async Task Reusing_a_rotated_token_after_the_grace_window_revokes_the_whole_session()
    {
        var user = await CreateUserAsync(_fixture.Factory.Services);
        var first = RefreshCookie(await LoginAsync(_client, user.UserName!, Password))!;
        var second = RefreshCookie(await PostWithCookieAsync(_client, "/api/auth/refresh", first))!;

        _fixture.Clock.Now = BaseTime.AddSeconds(11);
        var reuse = await PostWithCookieAsync(_client, "/api/auth/refresh", first);
        await AssertProblemAsync(reuse, HttpStatusCode.Unauthorized, "error.auth.invalid_refresh_token");

        // The newest token of the session is revoked too.
        var afterRevoke = await PostWithCookieAsync(_client, "/api/auth/refresh", second);
        await AssertProblemAsync(afterRevoke, HttpStatusCode.Unauthorized, "error.auth.invalid_refresh_token");
    }

    [Fact]
    public async Task Reusing_a_rotated_token_inside_the_grace_window_is_rejected_without_revoking()
    {
        var user = await CreateUserAsync(_fixture.Factory.Services);
        var first = RefreshCookie(await LoginAsync(_client, user.UserName!, Password))!;
        var second = RefreshCookie(await PostWithCookieAsync(_client, "/api/auth/refresh", first))!;

        _fixture.Clock.Now = BaseTime.AddSeconds(5);
        var concurrent = await PostWithCookieAsync(_client, "/api/auth/refresh", first);
        await AssertProblemAsync(concurrent, HttpStatusCode.Unauthorized, "error.auth.invalid_refresh_token");

        // The session survives: the newest token still works.
        var next = await PostWithCookieAsync(_client, "/api/auth/refresh", second);
        Assert.Equal(HttpStatusCode.OK, next.StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_the_session_and_clears_the_cookie()
    {
        var user = await CreateUserAsync(_fixture.Factory.Services);
        var token = RefreshCookie(await LoginAsync(_client, user.UserName!, Password))!;

        var logout = await PostWithCookieAsync(_client, "/api/auth/logout", token);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Contains("expires=Thu, 01 Jan 1970", SetCookie(logout)!, StringComparison.OrdinalIgnoreCase);

        var refresh = await PostWithCookieAsync(_client, "/api/auth/refresh", token);
        await AssertProblemAsync(refresh, HttpStatusCode.Unauthorized, "error.auth.invalid_refresh_token");
    }

    [Fact]
    public async Task Logout_without_a_cookie_is_still_204()
    {
        var logout = await PostWithCookieAsync(_client, "/api/auth/logout", cookieValue: null);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
    }

    [Fact]
    public async Task Expired_refresh_token_is_rejected()
    {
        var user = await CreateUserAsync(_fixture.Factory.Services);
        var token = RefreshCookie(await LoginAsync(_client, user.UserName!, Password))!;

        _fixture.Clock.Now = BaseTime.AddDays(7).AddMinutes(1);
        var refresh = await PostWithCookieAsync(_client, "/api/auth/refresh", token);

        await AssertProblemAsync(refresh, HttpStatusCode.Unauthorized, "error.auth.invalid_refresh_token");
    }

    [Fact]
    public async Task Session_ends_at_the_absolute_cap_even_when_refreshed_often()
    {
        var user = await CreateUserAsync(_fixture.Factory.Services);
        var token = RefreshCookie(await LoginAsync(_client, user.UserName!, Password))!;

        // Refresh every 6 days: each token is valid, but the session may not outlive 30 days.
        for (var day = 6; day <= 30; day += 6)
        {
            _fixture.Clock.Now = BaseTime.AddDays(day);
            var refreshed = await PostWithCookieAsync(_client, "/api/auth/refresh", token);
            if (day < 30)
            {
                Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
                token = RefreshCookie(refreshed)!;
            }
            else
            {
                await AssertProblemAsync(refreshed, HttpStatusCode.Unauthorized, "error.auth.invalid_refresh_token");
            }
        }
    }

    [Fact]
    public async Task Refresh_without_a_cookie_is_rejected_and_clears_the_cookie()
    {
        var refresh = await PostWithCookieAsync(_client, "/api/auth/refresh", cookieValue: null);

        await AssertProblemAsync(refresh, HttpStatusCode.Unauthorized, "error.auth.invalid_refresh_token");
        Assert.Contains("expires=Thu, 01 Jan 1970", SetCookie(refresh)!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Refresh_for_a_locked_user_revokes_the_session()
    {
        var user = await CreateUserAsync(_fixture.Factory.Services);
        var token = RefreshCookie(await LoginAsync(_client, user.UserName!, Password))!;

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var stored = (await users.FindByIdAsync(user.Id.ToString()))!;
            await users.SetLockoutEndDateAsync(stored, DateTimeOffset.UtcNow.AddHours(1));
        }

        var refresh = await PostWithCookieAsync(_client, "/api/auth/refresh", token);

        await AssertProblemAsync(refresh, HttpStatusCode.Unauthorized, "error.auth.invalid_refresh_token");
        using var verify = _fixture.Factory.Services.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<IAppDbContext>();
        var tokens = await db.RefreshTokens.AsNoTracking().Where(t => t.UserId == user.Id).ToListAsync();
        Assert.All(tokens, t => Assert.NotNull(t.RevokedAt));
    }

    [Fact]
    public async Task Refresh_token_is_stored_only_as_a_hash()
    {
        var user = await CreateUserAsync(_fixture.Factory.Services);
        var raw = RefreshCookie(await LoginAsync(_client, user.UserName!, Password))!;

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var stored = await db.RefreshTokens.AsNoTracking().SingleAsync(t => t.UserId == user.Id);

        Assert.NotEqual(raw, stored.TokenHash);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(raw))), stored.TokenHash);
    }

    [Fact]
    public async Task Refresh_from_a_foreign_origin_is_forbidden_and_a_same_or_allowed_origin_passes()
    {
        var user = await CreateUserAsync(_fixture.Factory.Services);
        var token = RefreshCookie(await LoginAsync(_client, user.UserName!, Password))!;

        var foreign = await PostWithCookieAsync(_client, "/api/auth/refresh", token, origin: "https://evil.example");
        await AssertProblemAsync(foreign, HttpStatusCode.Forbidden, "error.auth.forbidden");

        var nullOrigin = await PostWithCookieAsync(_client, "/api/auth/refresh", token, origin: "null");
        await AssertProblemAsync(nullOrigin, HttpStatusCode.Forbidden, "error.auth.forbidden");

        // The test server's own host (any scheme), then an origin from Auth:AllowedOrigins (Development).
        var sameHost = await PostWithCookieAsync(_client, "/api/auth/refresh", token, origin: "https://localhost");
        Assert.Equal(HttpStatusCode.OK, sameHost.StatusCode);
        var allowed = await PostWithCookieAsync(_client, "/api/auth/refresh", RefreshCookie(sameHost), origin: "http://localhost:4200");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    }

    [Fact]
    public async Task Logout_from_a_foreign_origin_is_forbidden_and_keeps_the_session()
    {
        var user = await CreateUserAsync(_fixture.Factory.Services);
        var token = RefreshCookie(await LoginAsync(_client, user.UserName!, Password))!;

        var logout = await PostWithCookieAsync(_client, "/api/auth/logout", token, origin: "https://evil.example");
        await AssertProblemAsync(logout, HttpStatusCode.Forbidden, "error.auth.forbidden");

        var refresh = await PostWithCookieAsync(_client, "/api/auth/refresh", token);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
    }
}
