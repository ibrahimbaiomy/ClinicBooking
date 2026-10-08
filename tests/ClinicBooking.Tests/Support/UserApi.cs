using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicBooking.Domain.Permissions;
using ClinicBooking.Infrastructure.Identity;
using ClinicBooking.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using static ClinicBooking.Tests.Support.AuthHelpers;

namespace ClinicBooking.Tests.Support;

/// <summary>Helpers for the user-management tests (D57).</summary>
internal static class UserApi
{
    // Long, random, and meets the policy; never reused between calls, so a leak is traceable to one call.
    public static string NewTemporaryPassword() => "Tmp-" + Guid.NewGuid().ToString("N") + "-Xy9";

    public static string NewUserName() => "u" + Guid.NewGuid().ToString("N")[..16];

    /// <summary>A signed-in administrator (global users.manage, plus any extra permissions).</summary>
    public static Task<(string Token, long UserId)> AdminAsync(
        AuthApiFixture fixture,
        HttpClient client,
        params string[] extra) =>
        SpecialtyHelpers.SignInAsync(fixture, client, [Permissions.Users.Manage, .. extra]);

    public static Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string url,
        string? token,
        object? body = null) =>
        client.SendAsync(SpecialtyHelpers.Json(method, url, token, body));

    public sealed record Created(long Id, string UserName, string Password);

    /// <summary>Creates a user through the API with a random temporary password.</summary>
    public static async Task<Created> CreateUserAsync(HttpClient client, string adminToken, string? password = null)
    {
        var userName = NewUserName();
        password ??= NewTemporaryPassword();

        var response = await SendAsync(
            client, HttpMethod.Post, "/api/users", adminToken, new { userName, temporaryPassword = password });
        await AssertStatusAsync(response, HttpStatusCode.Created);

        var body = await ProblemAsync(response);
        return new Created(body.GetProperty("id").GetInt64(), userName, password);
    }

    /// <summary>A user created through the API whose temporary password has already been replaced.</summary>
    public static async Task<(Created User, string Password, string Token)> ReadyUserAsync(
        HttpClient client,
        string adminToken)
    {
        var user = await CreateUserAsync(client, adminToken);
        var login = await LoginAsync(client, user.UserName, user.Password);
        await AssertStatusAsync(login, HttpStatusCode.OK);
        var token = await AccessTokenAsync(login);

        var changed = await ChangePasswordAsync(client, token, RefreshCookie(login), user.Password, Password);
        await AssertStatusAsync(changed, HttpStatusCode.NoContent);

        return (user, Password, token);
    }

    public static Task<HttpResponseMessage> ChangePasswordAsync(
        HttpClient client,
        string? token,
        string? cookie,
        string? current,
        string? next,
        string? origin = null)
    {
        var request = SpecialtyHelpers.Json(HttpMethod.Post, "/api/auth/change-password", token, new
        {
            currentPassword = current,
            newPassword = next
        });
        if (cookie is not null)
        {
            request.Headers.Add("Cookie", $"{CookieName}={cookie}");
        }

        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        return client.SendAsync(request);
    }

    public static async Task<JsonElement> DetailAsync(HttpClient client, string adminToken, long id)
    {
        var response = await SendAsync(client, HttpMethod.Get, $"/api/users/{id}", adminToken);
        await AssertStatusAsync(response, HttpStatusCode.OK);
        return await ProblemAsync(response);
    }

    public static string[] Strings(JsonElement array) =>
        array.EnumerateArray().Select(e => e.GetString()!).ToArray();

    /// <summary>Makes every active user other than <paramref name="keepId"/> that holds users.manage inactive, directly in the database.</summary>
    public static async Task DeactivateOtherAdministratorsAsync(IServiceProvider services, params long[] keepIds)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var adminIds = await db.UserClaims
            .Where(c => c.ClaimType == PermissionChecker.ClaimType && c.ClaimValue == Permissions.Users.Manage)
            .Select(c => c.UserId)
            .ToListAsync();

        foreach (var user in await db.Users.Where(u => adminIds.Contains(u.Id) && !keepIds.Contains(u.Id)).ToListAsync())
        {
            user.IsActive = false;
        }

        await db.SaveChangesAsync();
    }

    public static async Task SetActiveAsync(IServiceProvider services, long userId, bool isActive)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(u => u.Id == userId);
        user.IsActive = isActive;
        await db.SaveChangesAsync();
    }

    public static async Task<int> CountActiveAdministratorsAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.Users.CountAsync(u => u.IsActive
            && db.UserClaims.Any(c => c.UserId == u.Id
                && c.ClaimType == PermissionChecker.ClaimType
                && c.ClaimValue == Permissions.Users.Manage));
    }

    /// <summary>The problem body without the per-request ids, so two responses can be compared.</summary>
    public static async Task<string> ComparableAsync(HttpResponseMessage response)
    {
        var body = await ProblemAsync(response);
        var fields = body.EnumerateObject()
            .Where(p => p.Name is not ("correlationId" or "traceId"))
            .Select(p => $"{p.Name}={p.Value.GetRawText()}");

        return $"{(int)response.StatusCode}|{string.Join(",", fields)}";
    }
}
