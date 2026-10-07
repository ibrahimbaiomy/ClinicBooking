using System.Net;
using System.Text.Json;
using ClinicBooking.Domain.Entities;
using ClinicBooking.Domain.Permissions;
using ClinicBooking.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using static ClinicBooking.Tests.Support.AuthHelpers;
using static ClinicBooking.Tests.Support.SpecialtyHelpers;

namespace ClinicBooking.Tests.Support;

/// <summary>Helpers for the Doctors tests (D61).</summary>
internal static class DoctorHelpers
{
    public static async Task<long> NewClinicAsync(AuthApiFixture fixture, HttpClient client)
    {
        var manager = await ClinicHelpers.ClinicManagerTokenAsync(fixture, client);
        return (await ClinicHelpers.CreateClinicOkAsync(client, manager, UniqueArabic(), UniqueEnglish())).GetProperty("id").GetInt64();
    }

    public static async Task<long> NewSpecialtyAsync(AuthApiFixture fixture, HttpClient client, string? nameAr = null, string? nameEn = null)
    {
        var manager = await ManagerTokenAsync(fixture, client);
        return (await CreateOkAsync(client, manager, nameAr ?? UniqueArabic(), nameEn ?? UniqueEnglish())).GetProperty("id").GetInt64();
    }

    /// <summary>A signed-in user holding doctors.manage in the given clinics (rows added directly).</summary>
    public static async Task<(string Token, long UserId)> DoctorManagerAsync(
        AuthApiFixture fixture,
        HttpClient client,
        params long[] clinicIds)
    {
        var (token, userId) = await SignInAsync(fixture, client);
        foreach (var clinicId in clinicIds)
        {
            await GrantAsync(fixture.Factory.Services, userId, clinicId, Permissions.Doctors.Manage);
        }

        return (token, userId);
    }

    public static async Task GrantAsync(IServiceProvider services, long userId, long clinicId, string permission)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.UserClinicPermissions.Add(new UserClinicPermission { UserId = userId, ClinicId = clinicId, Permission = permission });
        await db.SaveChangesAsync();
    }

    /// <summary>Soft-deletes a clinic directly, as if it had been deleted before the doctor existed.</summary>
    public static async Task SoftDeleteClinicAsync(IServiceProvider services, long clinicId)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clinic = await db.Clinics.SingleAsync(c => c.Id == clinicId);
        db.Clinics.Remove(clinic);
        await db.SaveChangesAsync();
    }

    public static Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string url, string? token, object? body = null) =>
        client.SendAsync(Json(method, url, token, body));

    public static Task<HttpResponseMessage> CreateDoctorAsync(
        HttpClient client,
        string token,
        string nameAr,
        string nameEn,
        long[] specialtyIds,
        long[] clinicIds,
        int? slotMinutes = 15) =>
        SendAsync(client, HttpMethod.Post, "/api/doctors", token, new { nameAr, nameEn, specialtyIds, clinicIds, slotMinutes });

    public static async Task<JsonElement> CreateDoctorOkAsync(
        HttpClient client,
        string token,
        long[] specialtyIds,
        long[] clinicIds,
        string? nameAr = null,
        string? nameEn = null,
        int slotMinutes = 15)
    {
        var response = await CreateDoctorAsync(client, token, nameAr ?? UniqueArabic(), nameEn ?? UniqueEnglish(), specialtyIds, clinicIds, slotMinutes);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ProblemAsync(response);
    }

    public static Task<HttpResponseMessage> UpdateDoctorAsync(
        HttpClient client,
        string token,
        long id,
        string nameAr,
        string nameEn,
        long[] specialtyIds,
        string? rowVersion) =>
        SendAsync(client, HttpMethod.Put, $"/api/doctors/{id}", token, new { nameAr, nameEn, specialtyIds, rowVersion });

    /// <summary>Re-sends the doctor's own names and specialties with the given row version.</summary>
    public static Task<HttpResponseMessage> UpdateDoctorAsync(HttpClient client, string token, JsonElement doctor, string? rowVersion = null) =>
        UpdateDoctorAsync(
            client,
            token,
            doctor.GetProperty("id").GetInt64(),
            doctor.GetProperty("nameAr").GetString()!,
            doctor.GetProperty("nameEn").GetString()!,
            SpecialtyIds(doctor),
            rowVersion ?? doctor.GetProperty("rowVersion").GetString());

    public static async Task<JsonElement> GetDoctorAsync(HttpClient client, string token, long id)
    {
        var response = await SendAsync(client, HttpMethod.Get, $"/api/doctors/{id}", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ProblemAsync(response);
    }

    public static async Task<JsonElement> ListDoctorsAsync(HttpClient client, string token, string query)
    {
        var response = await SendAsync(client, HttpMethod.Get, $"/api/doctors?{query}", token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ProblemAsync(response);
    }

    public static long[] Ids(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt64()).ToArray();

    public static long[] SpecialtyIds(JsonElement doctor) =>
        doctor.GetProperty("specialties").EnumerateArray().Select(s => s.GetProperty("id").GetInt64()).Order().ToArray();

    public static (long ClinicId, bool IsActive)[] Assignments(JsonElement doctor) =>
        doctor.GetProperty("clinics").EnumerateArray()
            .Select(c => (c.GetProperty("clinicId").GetInt64(), c.GetProperty("isActive").GetBoolean()))
            .OrderBy(c => c.Item1)
            .ToArray();

    public static async Task<string> FieldErrorAsync(HttpResponseMessage response, string field)
    {
        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "error.validation.failed");
        return (await ProblemAsync(response)).GetProperty("errors").GetProperty(field)[0].GetString()!;
    }
}
