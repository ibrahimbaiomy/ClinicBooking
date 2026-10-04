using System.Text.Json;
using ClinicBooking.Domain.Permissions;
using static ClinicBooking.Tests.Support.AuthHelpers;

namespace ClinicBooking.Tests.Support;

/// <summary>Clinics counterparts of <see cref="SpecialtyHelpers"/> (which still provides Json, UniqueArabic, SignInAsync).</summary>
internal static class ClinicHelpers
{
    public static async Task<string> ClinicManagerTokenAsync(AuthApiFixture fixture, HttpClient client) =>
        (await SpecialtyHelpers.SignInAsync(fixture, client, Permissions.Clinics.Manage)).Token;

    public static Task<HttpResponseMessage> CreateClinicAsync(
        HttpClient client,
        string token,
        string nameAr,
        string nameEn,
        string? address = null,
        string? phone = null) =>
        client.SendAsync(SpecialtyHelpers.Json(HttpMethod.Post, "/api/clinics", token, new { nameAr, nameEn, address, phone }));

    public static async Task<JsonElement> CreateClinicOkAsync(
        HttpClient client,
        string token,
        string nameAr,
        string nameEn,
        string? address = null,
        string? phone = null)
    {
        var response = await CreateClinicAsync(client, token, nameAr, nameEn, address, phone);
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);
        return await ProblemAsync(response);
    }

    public static Task<HttpResponseMessage> UpdateClinicAsync(
        HttpClient client,
        string token,
        long id,
        string nameAr,
        string nameEn,
        string? address,
        string? phone,
        string? rowVersion) =>
        client.SendAsync(SpecialtyHelpers.Json(HttpMethod.Put, $"/api/clinics/{id}", token, new { nameAr, nameEn, address, phone, rowVersion }));

    public static async Task<JsonElement> ListClinicsAsync(HttpClient client, string token, string query)
    {
        var response = await client.SendAsync(SpecialtyHelpers.Json(HttpMethod.Get, $"/api/clinics?{query}", token));
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        return await ProblemAsync(response);
    }
}
