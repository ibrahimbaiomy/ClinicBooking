using System.Net.Http.Json;
using System.Text.Json;
using ClinicBooking.Domain.Permissions;
using ClinicBooking.Infrastructure.Identity;
using static ClinicBooking.Tests.Support.AuthHelpers;

namespace ClinicBooking.Tests.Support;

internal static class SpecialtyHelpers
{
    // Arabic letters that no folding touches, so a generated word never collides by accident.
    private const string StableLetters = "بتثجحخدذرزسشصضطظعغفقكلمن";

    public static string UniqueArabic(int length = 10)
    {
        var bytes = Guid.NewGuid().ToByteArray();
        return new string(Enumerable.Range(0, length).Select(i => StableLetters[bytes[i % bytes.Length] % StableLetters.Length]).ToArray())
            + new string(Enumerable.Range(0, 4).Select(i => StableLetters[Random.Shared.Next(StableLetters.Length)]).ToArray());
    }

    public static string UniqueEnglish() => Guid.NewGuid().ToString("N");

    /// <summary>A user with the permissions, logged in; returns its access token and id.</summary>
    public static async Task<(string Token, long UserId)> SignInAsync(
        AuthApiFixture fixture,
        HttpClient client,
        params string[] permissions)
    {
        var user = await CreateUserAsync(fixture.Factory.Services, permissions);
        var login = await LoginAsync(client, user.UserName!, Password);
        return (await AccessTokenAsync(login), user.Id);
    }

    public static async Task<string> ManagerTokenAsync(AuthApiFixture fixture, HttpClient client) =>
        (await SignInAsync(fixture, client, Permissions.Specialties.Manage)).Token;

    public static HttpRequestMessage Json(HttpMethod method, string url, string? token, object? body = null)
    {
        var request = WithBearer(method, url, token);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    public static Task<HttpResponseMessage> CreateAsync(HttpClient client, string token, string nameAr, string nameEn) =>
        client.SendAsync(Json(HttpMethod.Post, "/api/specialties", token, new { nameAr, nameEn }));

    public static async Task<JsonElement> CreateOkAsync(HttpClient client, string token, string nameAr, string nameEn)
    {
        var response = await CreateAsync(client, token, nameAr, nameEn);
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        return await ProblemAsync(response);
    }

    public static Task<HttpResponseMessage> UpdateAsync(HttpClient client, string token, long id, string nameAr, string nameEn, string? rowVersion) =>
        client.SendAsync(Json(HttpMethod.Put, $"/api/specialties/{id}", token, new { nameAr, nameEn, rowVersion }));

    public static async Task<JsonElement> ListAsync(HttpClient client, string token, string query)
    {
        var response = await client.SendAsync(Json(HttpMethod.Get, $"/api/specialties?{query}", token));
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return await ProblemAsync(response);
    }

    public static string[] NamesEn(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("nameEn").GetString()!).ToArray();

    public static string[] NamesAr(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("nameAr").GetString()!).ToArray();
}
