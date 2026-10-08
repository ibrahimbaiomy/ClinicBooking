using System.Net;
using System.Text.Json;
using ClinicBooking.Domain.Exceptions;
using ClinicBooking.Domain.Permissions;
using ClinicBooking.Tests.Support;
using static ClinicBooking.Tests.Support.AuthHelpers;
using static ClinicBooking.Tests.Support.PatientHelpers;
using static ClinicBooking.Tests.Support.SpecialtyHelpers;

namespace ClinicBooking.Tests;

/// <summary>
/// The duplicate-phone warning (D44, D63): a 409 with the matches, nothing saved, until the client confirms.
/// The matches are shown only to a caller who may read patients.
/// </summary>
[Collection(SqlServerCollection.Name)]
public class PatientsDuplicatePhoneTests : IClassFixture<AuthApiFixture>
{
    private readonly AuthApiFixture _fixture;
    private readonly HttpClient _client;

    public PatientsDuplicatePhoneTests(AuthApiFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Factory.CreateClient();
    }

    private async Task<int> CountWithPhoneAsync(string token, string e164) =>
        (await ListPatientsAsync(_client, token, $"pageSize=100&search={Uri.EscapeDataString(e164)}")).GetProperty("items")
            .EnumerateArray().Count(p => p.GetProperty("phone").GetString() == e164);

    [Fact]
    public async Task Create_with_a_phone_in_use_is_409_with_the_matches_and_saves_nothing()
    {
        var token = await PatientManagerAsync(_fixture, _client);
        var phone = UniqueMobile();
        var first = await CreatePatientOkAsync(_client, token, "سارة " + UniqueArabic(), phone);

        // Typed differently, the same number once normalised.
        var response = await CreatePatientAsync(_client, token, UniqueArabic(), "+20 " + phone[1..]);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "error.patient.phone_exists");
        var body = await ProblemAsync(response);
        Assert.Equal(1, body.GetProperty("matchCount").GetInt32());
        var match = Assert.Single(body.GetProperty("matches").EnumerateArray());
        Assert.Equal(first.GetProperty("id").GetInt64(), match.GetProperty("id").GetInt64());
        Assert.Equal(first.GetProperty("name").GetString(), match.GetProperty("name").GetString());
        Assert.Equal(E164(phone), match.GetProperty("phone").GetString());
        Assert.Equal(1, await CountWithPhoneAsync(token, E164(phone)));
    }

    [Fact]
    public async Task Resending_with_confirmDuplicatePhone_saves_anyway()
    {
        var token = await PatientManagerAsync(_fixture, _client);
        var phone = UniqueMobile();
        await CreatePatientOkAsync(_client, token, phone: phone);

        var response = await CreatePatientAsync(_client, token, UniqueArabic(), phone, confirmDuplicatePhone: true);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(2, await CountWithPhoneAsync(token, E164(phone)));
    }

    [Fact]
    public async Task The_flag_is_ignored_when_there_is_no_match()
    {
        var token = await PatientManagerAsync(_fixture, _client);

        var confirmed = await CreatePatientAsync(_client, token, UniqueArabic(), UniqueMobile(), confirmDuplicatePhone: true);
        var unconfirmed = await CreatePatientAsync(_client, token, UniqueArabic(), UniqueMobile(), confirmDuplicatePhone: false);

        Assert.Equal(HttpStatusCode.Created, confirmed.StatusCode);
        Assert.Equal(HttpStatusCode.Created, unconfirmed.StatusCode);
    }

    [Fact]
    public async Task At_most_five_matches_ordered_by_name_and_the_full_count()
    {
        var token = await PatientManagerAsync(_fixture, _client);
        var phone = UniqueMobile();
        var unique = UniqueEnglish();
        foreach (var name in new[] { "F", "B", "D", "A", "E", "C" })
        {
            Assert.Equal(HttpStatusCode.Created, (await CreatePatientAsync(_client, token, $"{name} {unique}", phone, true)).StatusCode);
        }

        var body = await ProblemAsync(await CreatePatientAsync(_client, token, UniqueArabic(), phone));

        Assert.Equal(6, body.GetProperty("matchCount").GetInt32());
        Assert.Equal(
            ["A", "B", "C", "D", "E"],
            body.GetProperty("matches").EnumerateArray().Select(m => m.GetProperty("name").GetString()![..1]).ToArray());
    }

    [Fact]
    public async Task A_deleted_patient_with_the_phone_does_not_count()
    {
        var token = await PatientManagerAsync(_fixture, _client);
        var phone = UniqueMobile();
        var old = await CreatePatientOkAsync(_client, token, phone: phone);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.SendAsync(Json(HttpMethod.Delete, $"/api/patients/{old.GetProperty("id").GetInt64()}", token))).StatusCode);

        Assert.Equal(HttpStatusCode.Created, (await CreatePatientAsync(_client, token, UniqueArabic(), phone)).StatusCode);
    }

    [Fact]
    public async Task Without_patients_read_the_409_carries_the_count_and_no_name_or_phone()
    {
        var reader = await PatientManagerAsync(_fixture, _client);
        var (creatorOnly, _) = await SignInAsync(_fixture, _client, Permissions.Patients.Create);
        var phone = UniqueMobile();
        var name = "خديجة " + UniqueArabic();
        await CreatePatientOkAsync(_client, reader, name, phone);

        var response = await CreatePatientAsync(_client, creatorOnly, UniqueArabic(), phone);

        await AssertProblemAsync(response, HttpStatusCode.Conflict, "error.patient.phone_exists");
        var raw = await response.Content.ReadAsStringAsync();
        var body = JsonDocument.Parse(raw).RootElement;
        Assert.Equal(1, body.GetProperty("matchCount").GetInt32());
        Assert.False(body.TryGetProperty("matches", out _));
        Assert.DoesNotContain(name, raw);
        Assert.DoesNotContain(phone[1..], raw); // neither the national nor the E.164 form
    }

    [Fact]
    public async Task With_patients_read_and_create_the_409_carries_both()
    {
        var reader = await PatientManagerAsync(_fixture, _client);
        var (creatorReader, _) = await SignInAsync(_fixture, _client, Permissions.Patients.Create, Permissions.Patients.Read);
        var phone = UniqueMobile();
        await CreatePatientOkAsync(_client, reader, phone: phone);

        var body = await ProblemAsync(await CreatePatientAsync(_client, creatorReader, UniqueArabic(), phone));

        Assert.Equal(1, body.GetProperty("matchCount").GetInt32());
        Assert.Single(body.GetProperty("matches").EnumerateArray());
    }

    [Fact]
    public async Task Edit_without_a_phone_change_never_warns()
    {
        var token = await PatientManagerAsync(_fixture, _client);
        var phone = UniqueMobile();
        await CreatePatientOkAsync(_client, token, phone: phone);
        var created = await ProblemAsync(await CreatePatientAsync(_client, token, UniqueArabic(), phone, confirmDuplicatePhone: true));

        // Same number, typed in another form: still not a change.
        var response = await UpdatePatientAsync(
            _client, token, created.GetProperty("id").GetInt64(), "اسم جديد", "+20" + phone[1..], created.GetProperty("rowVersion").GetString());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Edit_to_a_phone_in_use_warns_leaves_the_patient_unchanged_and_saves_when_confirmed()
    {
        var token = await PatientManagerAsync(_fixture, _client);
        var taken = UniqueMobile();
        var other = await CreatePatientOkAsync(_client, token, phone: taken);
        var mine = await CreatePatientOkAsync(_client, token);
        var id = mine.GetProperty("id").GetInt64();
        var rowVersion = mine.GetProperty("rowVersion").GetString();

        var warned = await UpdatePatientAsync(_client, token, id, "اسم", taken, rowVersion);
        await AssertProblemAsync(warned, HttpStatusCode.Conflict, "error.patient.phone_exists");
        var match = Assert.Single((await ProblemAsync(warned)).GetProperty("matches").EnumerateArray());
        Assert.Equal(other.GetProperty("id").GetInt64(), match.GetProperty("id").GetInt64()); // never itself
        var unchanged = await ProblemAsync(await _client.SendAsync(Json(HttpMethod.Get, $"/api/patients/{id}", token)));
        Assert.Equal(mine.GetProperty("phone").GetString(), unchanged.GetProperty("phone").GetString());

        var saved = await UpdatePatientAsync(_client, token, id, "اسم", taken, rowVersion, confirmDuplicatePhone: true);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        Assert.Equal(E164(taken), (await ProblemAsync(saved)).GetProperty("phone").GetString());
    }

    [Fact]
    public void The_exception_message_is_the_key_only()
    {
        var failure = new DuplicatePhoneException(1, [new PhoneMatch(1, "سارة", "+201012345678")]);

        Assert.Equal("error.patient.phone_exists", failure.Message);
        Assert.DoesNotContain("سارة", failure.ToString());
        Assert.DoesNotContain("+201012345678", failure.ToString());
    }
}
