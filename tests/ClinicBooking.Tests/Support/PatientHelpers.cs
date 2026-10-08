using System.Net;
using System.Text.Json;
using ClinicBooking.Domain.Permissions;
using static ClinicBooking.Tests.Support.AuthHelpers;
using static ClinicBooking.Tests.Support.SpecialtyHelpers;

namespace ClinicBooking.Tests.Support;

/// <summary>Helpers for the Patients tests (D63).</summary>
internal static class PatientHelpers
{
    public static readonly string[] AllPatientPermissions =
        [Permissions.Patients.Read, Permissions.Patients.Create, Permissions.Patients.Edit, Permissions.Patients.Delete];

    /// <summary>A user with every patients.* permission.</summary>
    public static async Task<string> PatientManagerAsync(AuthApiFixture fixture, HttpClient client) =>
        (await SignInAsync(fixture, client, AllPatientPermissions)).Token;

    private static int _counter = Random.Shared.Next(1_000_000);

    /// <summary>A valid Egyptian mobile number nobody else in the run uses, as typed (national form).</summary>
    public static string UniqueMobile() => "010" + (Interlocked.Increment(ref _counter) % 100_000_000).ToString("D8");

    /// <summary>The E.164 form of a national number from <see cref="UniqueMobile"/>.</summary>
    public static string E164(string national) => "+20" + national[1..];

    public static Task<HttpResponseMessage> CreatePatientAsync(
        HttpClient client,
        string token,
        string? name,
        string? phone,
        bool? confirmDuplicatePhone = null) =>
        client.SendAsync(Json(HttpMethod.Post, "/api/patients", token, new { name, phone, confirmDuplicatePhone }));

    public static async Task<JsonElement> CreatePatientOkAsync(HttpClient client, string token, string? name = null, string? phone = null)
    {
        var response = await CreatePatientAsync(client, token, name ?? UniqueArabic(), phone ?? UniqueMobile());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ProblemAsync(response);
    }

    public static Task<HttpResponseMessage> UpdatePatientAsync(
        HttpClient client,
        string token,
        long id,
        string? name,
        string? phone,
        string? rowVersion,
        bool? confirmDuplicatePhone = null) =>
        client.SendAsync(Json(HttpMethod.Put, $"/api/patients/{id}", token, new { name, phone, rowVersion, confirmDuplicatePhone }));

    public static async Task<JsonElement> ListPatientsAsync(HttpClient client, string token, string query)
    {
        var response = await client.SendAsync(Json(HttpMethod.Get, $"/api/patients?{query}", token));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ProblemAsync(response);
    }

    public static long[] PatientIds(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt64()).ToArray();
}
