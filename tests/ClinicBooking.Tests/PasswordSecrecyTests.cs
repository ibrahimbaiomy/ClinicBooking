using System.Net;
using ClinicBooking.Tests.Support;
using static ClinicBooking.Tests.Support.AuthHelpers;

namespace ClinicBooking.Tests;

/// <summary>
/// A temporary or new password travels only in a request body: it is never in a response, a header or a
/// log line, on success or on failure (D57). The log is the real console output of the host.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class PasswordSecrecyTests : IClassFixture<LogCaptureAuthFixture>
{
    private readonly LogCaptureAuthFixture _fixture;
    private readonly HttpClient _client;

    public PasswordSecrecyTests(LogCaptureAuthFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Factory.CreateClient();
    }

    private static async Task<string> EverythingAsync(HttpResponseMessage response)
    {
        var headers = string.Join("\n", response.Headers.Select(h => $"{h.Key}: {string.Join(",", h.Value)}"));
        // Set-Cookie of login is expected to carry the refresh token, not a password: it is still scanned.
        return headers + "\n" + await response.Content.ReadAsStringAsync();
    }

    [Fact]
    public async Task No_password_appears_in_any_response_or_log_line_on_success_or_failure()
    {
        var secrets = new List<string>();
        var texts = new List<string>();
        var names = new List<string>();

        async Task<HttpResponseMessage> TrackAsync(Task<HttpResponseMessage> call, params string[] secretsUsed)
        {
            var response = await call;
            secrets.AddRange(secretsUsed);
            texts.Add(await EverythingAsync(response));
            return response;
        }

        var (admin, adminId) = await UserApi.AdminAsync(_fixture, _client);

        // Create: success, duplicate user name, weak password.
        var created = UserApi.NewTemporaryPassword();
        var userName = UserApi.NewUserName();
        names.Add(userName);
        var createResponse = await TrackAsync(
            UserApi.SendAsync(_client, HttpMethod.Post, "/api/users", admin, new { userName, temporaryPassword = created }), created);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var userId = (await ProblemAsync(createResponse)).GetProperty("id").GetInt64();

        var duplicatePassword = UserApi.NewTemporaryPassword();
        await TrackAsync(
            UserApi.SendAsync(_client, HttpMethod.Post, "/api/users", admin, new { userName, temporaryPassword = duplicatePassword }),
            duplicatePassword);
        const string weak = "weakpassword-SECRETMARKER";
        await TrackAsync(
            UserApi.SendAsync(_client, HttpMethod.Post, "/api/users", admin, new { userName = UserApi.NewUserName(), temporaryPassword = weak }),
            weak);

        // Reset: success, weak, own account.
        var reset = UserApi.NewTemporaryPassword();
        var resetResponse = await TrackAsync(
            UserApi.SendAsync(_client, HttpMethod.Post, $"/api/users/{userId}/reset-password", admin, new { temporaryPassword = reset }), reset);
        Assert.Equal(HttpStatusCode.NoContent, resetResponse.StatusCode);
        const string weakReset = "short-RESETMARKER";
        await TrackAsync(
            UserApi.SendAsync(_client, HttpMethod.Post, $"/api/users/{userId}/reset-password", admin, new { temporaryPassword = weakReset }),
            weakReset);
        var own = UserApi.NewTemporaryPassword();
        await TrackAsync(
            UserApi.SendAsync(_client, HttpMethod.Post, $"/api/users/{adminId}/reset-password", admin, new { temporaryPassword = own }), own);

        // Sign in with the temporary password, then change-password: wrong current, weak new, success.
        var login = await TrackAsync(LoginAsync(_client, userName, reset));
        var token = await AccessTokenAsync(login);
        var cookie = RefreshCookie(login);
        const string wrongCurrent = "Wrong-CURRENTMARKER-1";
        var next = "Next-" + Guid.NewGuid().ToString("N") + "-Zz9";
        await TrackAsync(UserApi.ChangePasswordAsync(_client, token, cookie, wrongCurrent, next), wrongCurrent, next);
        const string weakNew = "weak-NEWMARKER";
        await TrackAsync(UserApi.ChangePasswordAsync(_client, token, cookie, reset, weakNew), weakNew);
        var changed = await TrackAsync(UserApi.ChangePasswordAsync(_client, token, cookie, reset, next), reset, next);
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

        // Details and lists, which must never carry a password either.
        await TrackAsync(UserApi.SendAsync(_client, HttpMethod.Get, $"/api/users/{userId}", admin));
        await TrackAsync(UserApi.SendAsync(_client, HttpMethod.Get, "/api/users", admin));
        await TrackAsync(UserApi.SendAsync(_client, HttpMethod.Get, "/api/auth/me", token));

        var log = _fixture.Output;

        // The capture works: lines of this very run are in it.
        Assert.Contains("created by", log);
        Assert.Contains("reset by", log);
        Assert.Contains("Password changed by user", log);

        foreach (var secret in secrets.Distinct())
        {
            Assert.DoesNotContain(secret, log);
            Assert.All(texts, text => Assert.DoesNotContain(secret, text));
        }

        // Authorization and user-management log lines carry ids and permission names, not user names.
        foreach (var name in names)
        {
            Assert.DoesNotContain(name, log);
        }
    }
}
