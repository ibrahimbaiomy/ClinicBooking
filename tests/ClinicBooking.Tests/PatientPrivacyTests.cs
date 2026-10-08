using System.Net;
using System.Text.Json;
using ClinicBooking.Tests.Support;
using static ClinicBooking.Tests.Support.AuthHelpers;
using static ClinicBooking.Tests.Support.PatientHelpers;
using static ClinicBooking.Tests.Support.SpecialtyHelpers;

namespace ClinicBooking.Tests;

/// <summary>
/// No patient name or phone reaches a log line (D38, D45, D63): the host's real console output is scanned after
/// a create, an edit, name and phone searches, the duplicate-phone 409 and a delete, as D57 does for passwords.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class PatientPrivacyTests : IClassFixture<LogCaptureAuthFixture>
{
    private readonly LogCaptureAuthFixture _fixture;
    private readonly HttpClient _client;

    public PatientPrivacyTests(LogCaptureAuthFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Factory.CreateClient();
    }

    [Fact]
    public async Task No_patient_name_or_phone_is_ever_logged()
    {
        var token = await PatientManagerAsync(_fixture, _client);
        var name = "Zubaida Qassemi " + UniqueEnglish()[..8];
        var arabicName = "زبيدة القاسمي " + UniqueArabic();
        var phone = UniqueMobile();
        var otherPhone = UniqueMobile();

        // Create, and a duplicate-phone 409 (its body carries the match; the log must not).
        var created = await CreatePatientOkAsync(_client, token, name, phone);
        var duplicate = await CreatePatientAsync(_client, token, arabicName, phone);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Contains(name, await duplicate.Content.ReadAsStringAsync());

        // Edit (name and phone change), then searches by name and by phone.
        var id = created.GetProperty("id").GetInt64();
        var edited = await UpdatePatientAsync(_client, token, id, arabicName, otherPhone, created.GetProperty("rowVersion").GetString());
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        await ListPatientsAsync(_client, token, $"search={Uri.EscapeDataString(arabicName)}");
        await ListPatientsAsync(_client, token, $"search={Uri.EscapeDataString(name)}");
        await ListPatientsAsync(_client, token, $"search={otherPhone}");
        await ListPatientsAsync(_client, token, $"search={Uri.EscapeDataString(E164(phone))}");

        // A validation failure echoes nothing either, and the delete.
        Assert.Equal(HttpStatusCode.BadRequest, (await CreatePatientAsync(_client, token, name, "not-" + phone)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.SendAsync(Json(HttpMethod.Delete, $"/api/patients/{id}", token))).StatusCode);

        var log = _fixture.Output;

        // The capture works: request lines and the 409's key line of this very run are in it.
        Assert.Contains("/api/patients", log);
        Assert.Contains("error.patient.phone_exists", log);

        foreach (var secret in new[] { name, arabicName, phone, otherPhone, E164(phone), E164(otherPhone), phone[1..], otherPhone[1..] })
        {
            Assert.DoesNotContain(secret, log);
            Assert.DoesNotContain(JsonSerializer.Serialize(secret).Trim('"'), log); // also when JSON-escaped
        }

        foreach (var word in name.Split(' ').Concat(arabicName.Split(' ')))
        {
            Assert.DoesNotContain(word, log);
        }
    }
}
