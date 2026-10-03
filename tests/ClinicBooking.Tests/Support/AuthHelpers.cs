using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using ClinicBooking.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicBooking.Tests.Support;

internal static class AuthHelpers
{
    public const string Password = "Correct-Horse-9-Battery";
    public const string CookieName = "refresh_token";

    public static async Task<ApplicationUser> CreateUserAsync(IServiceProvider services, params string[] permissions)
    {
        using var scope = services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var user = new ApplicationUser { UserName = $"user_{Guid.NewGuid():N}" };
        var created = await users.CreateAsync(user, Password);
        Assert.True(created.Succeeded, string.Join(", ", created.Errors.Select(e => e.Code)));

        foreach (var permission in permissions)
        {
            await users.AddClaimAsync(user, new Claim(PermissionChecker.ClaimType, permission));
        }

        return user;
    }

    public static Task<HttpResponseMessage> LoginAsync(HttpClient client, string userName, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new { userName, password });

    /// <summary>The raw refresh token from the Set-Cookie header, or null when absent.</summary>
    public static string? RefreshCookie(HttpResponseMessage response) =>
        SetCookie(response)?.Split(';')[0][(CookieName.Length + 1)..] is { Length: > 0 } value ? value : null;

    public static string? SetCookie(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(v => v.StartsWith(CookieName + "=", StringComparison.Ordinal))
            : null;

    /// <summary>Cookies are sent by hand: the Secure cookie would not travel over http through CookieContainer.</summary>
    public static Task<HttpResponseMessage> PostWithCookieAsync(
        HttpClient client,
        string path,
        string? cookieValue,
        string? origin = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (cookieValue is not null)
        {
            request.Headers.Add("Cookie", $"{CookieName}={cookieValue}");
        }

        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        return client.SendAsync(request);
    }

    public static HttpRequestMessage WithBearer(HttpMethod method, string path, string? token)
    {
        var request = new HttpRequestMessage(method, path);
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    public static async Task<string> AccessTokenAsync(HttpResponseMessage response)
    {
        var body = await ProblemAsync(response);
        return body.GetProperty("accessToken").GetString()!;
    }

    public static async Task<JsonElement> ProblemAsync(HttpResponseMessage response)
    {
        // Buffered so a response can be read more than once.
        await response.Content.LoadIntoBufferAsync();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    public static async Task AssertProblemAsync(HttpResponseMessage response, System.Net.HttpStatusCode status, string key)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await ProblemAsync(response);
        Assert.Equal(key, problem.GetProperty("title").GetString());
        Assert.False(string.IsNullOrEmpty(problem.GetProperty("correlationId").GetString()));
    }
}
