using System.Net;
using System.Text.Json;
using ClinicBooking.Application.Interfaces;
using ClinicBooking.Domain.Entities;
using ClinicBooking.Domain.Exceptions;
using ClinicBooking.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using static ClinicBooking.Tests.Support.AuthHelpers;
using static ClinicBooking.Tests.Support.PatientHelpers;
using static ClinicBooking.Tests.Support.SpecialtyHelpers;

namespace ClinicBooking.Tests;

/// <summary>Patients: validation, create, read, full-replace update, soft delete, audit (D44, D63).</summary>
[Collection(SqlServerCollection.Name)]
public class PatientsCrudTests : IClassFixture<AuthApiFixture>
{
    private static readonly DateTimeOffset BaseTime = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly AuthApiFixture _fixture;
    private readonly HttpClient _client;

    public PatientsCrudTests(AuthApiFixture fixture)
    {
        _fixture = fixture;
        _fixture.Clock.Now = BaseTime;
        _client = fixture.Factory.CreateClient();
    }

    // ---- validation -------------------------------------------------------------------------

    public static TheoryData<string?, string?, string, string> Invalid => new()
    {
        { "", "01012345678", "name", "error.patient.name_required" },
        { "   ", "01012345678", "name", "error.patient.name_required" },
        { null, "01012345678", "name", "error.patient.name_required" },
        { new string('ب', 101), "01012345678", "name", "error.patient.name_too_long" },
        { "ـً", "01012345678", "name", "error.patient.name_invalid" },
        { "أحمد", null, "phone", "error.patient.phone_required" },
        { "أحمد", "  ", "phone", "error.patient.phone_required" },
        { "أحمد", "12345", "phone", "error.patient.phone_invalid" },
        { "أحمد", "01312345678", "phone", "error.patient.phone_invalid" },
        { "أحمد", "01012345678" + new string(' ', 30), "phone", "error.patient.phone_invalid" }
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public async Task Create_rejects_invalid_input_with_keys(string? name, string? phone, string field, string key)
    {
        var token = await PatientManagerAsync(_fixture, _client);

        var response = await CreatePatientAsync(_client, token, name, phone);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "error.validation.failed");
        Assert.Equal(key, (await ProblemAsync(response)).GetProperty("errors").GetProperty(field)[0].GetString());
    }

    [Theory]
    [InlineData(null, "error.concurrency.row_version_required")]
    [InlineData("AAAA", "error.concurrency.row_version_invalid")]
    public async Task Update_requires_a_valid_row_version(string? rowVersion, string key)
    {
        var token = await PatientManagerAsync(_fixture, _client);
        var created = await CreatePatientOkAsync(_client, token);

        var response = await UpdatePatientAsync(_client, token, created.GetProperty("id").GetInt64(), "اسم", UniqueMobile(), rowVersion);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "error.validation.failed");
        Assert.Equal(key, (await ProblemAsync(response)).GetProperty("errors").GetProperty("rowVersion")[0].GetString());
    }

    [Theory]
    [InlineData("sortBy=nameAr", "sortBy", "error.sort.invalid")]
    [InlineData("pageSize=101", "pageSize", "error.paging.page_size_invalid")]
    [InlineData("page=0", "page", "error.paging.page_invalid")]
    public async Task List_rejects_invalid_parameters_with_keys(string query, string field, string key)
    {
        var token = await PatientManagerAsync(_fixture, _client);

        var response = await _client.SendAsync(Json(HttpMethod.Get, $"/api/patients?{query}", token));

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "error.validation.failed");
        Assert.Equal(key, (await ProblemAsync(response)).GetProperty("errors").GetProperty(field)[0].GetString());
    }

    // A value that slips past the validator (a caller other than the controller) is a 400 key, never a 500 (D55).
    [Theory]
    [InlineData("أحمد", "not a phone", "error.patient.phone_invalid")]
    [InlineData("أحمد", "", "error.patient.phone_required")]
    [InlineData("  ", "01012345678", "error.patient.name_required")]
    public void A_value_that_bypasses_the_validator_is_an_InvalidRequestException(string name, string phone, string key)
    {
        var failure = Assert.Throws<InvalidRequestException>(() => Patient.Create(name, phone));

        Assert.Equal(key, failure.ErrorKey);
    }

    [Fact]
    public void The_phone_key_is_the_callers_choice_so_clinics_keep_theirs()
    {
        var clinic = Assert.Throws<InvalidRequestException>(() => Clinic.Create("أ", "A", null, "nope"));
        var patient = Assert.Throws<InvalidRequestException>(() => Patient.Create("أ", "nope"));

        Assert.Equal("error.clinic.phone_invalid", clinic.ErrorKey);
        Assert.Equal("error.patient.phone_invalid", patient.ErrorKey);
    }

    // ---- create, read, update, delete -------------------------------------------------------

    [Fact]
    public async Task Create_returns_201_with_a_location_the_trimmed_name_and_the_E164_phone()
    {
        var token = await PatientManagerAsync(_fixture, _client);
        var name = UniqueArabic();

        var response = await CreatePatientAsync(_client, token, $"  {name} ", "٠١٠ ٢٢٣٣ ٤٤٥٥");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ProblemAsync(response);
        var id = body.GetProperty("id").GetInt64();
        Assert.Equal($"/api/patients/{id}", response.Headers.Location!.AbsolutePath);
        Assert.Equal(name, body.GetProperty("name").GetString());
        Assert.Equal("+201022334455", body.GetProperty("phone").GetString());
        Assert.Equal(BaseTime, body.GetProperty("createdAt").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("updatedAt").ValueKind);
        Assert.Equal(12, body.GetProperty("rowVersion").GetString()!.Length);
        Assert.False(body.TryGetProperty("nameNormalized", out _));
        Assert.False(body.TryGetProperty("isDeleted", out _));
        Assert.False(body.TryGetProperty("createdBy", out _));
    }

    [Fact]
    public async Task Patients_may_share_a_name()
    {
        var token = await PatientManagerAsync(_fixture, _client);
        var name = UniqueArabic();
        await CreatePatientOkAsync(_client, token, name);

        Assert.Equal(HttpStatusCode.Created, (await CreatePatientAsync(_client, token, name, UniqueMobile())).StatusCode);
    }

    [Fact]
    public async Task Get_returns_the_patient_and_404_for_an_unknown_id()
    {
        var token = await PatientManagerAsync(_fixture, _client);
        var created = await CreatePatientOkAsync(_client, token);

        var found = await _client.SendAsync(Json(HttpMethod.Get, $"/api/patients/{created.GetProperty("id").GetInt64()}", token));
        var missing = await _client.SendAsync(Json(HttpMethod.Get, "/api/patients/987654321", token));

        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        await AssertProblemAsync(missing, HttpStatusCode.NotFound, "error.patient.not_found");
    }

    [Fact]
    public async Task Update_replaces_both_values_and_the_row_version()
    {
        var token = await PatientManagerAsync(_fixture, _client);
        var created = await CreatePatientOkAsync(_client, token);
        var id = created.GetProperty("id").GetInt64();
        var phone = UniqueMobile();
        _fixture.Clock.Now = BaseTime.AddMinutes(5);

        var response = await UpdatePatientAsync(_client, token, id, " New Name ", phone, created.GetProperty("rowVersion").GetString());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await ProblemAsync(response);
        Assert.Equal("New Name", updated.GetProperty("name").GetString());
        Assert.Equal(E164(phone), updated.GetProperty("phone").GetString());
        Assert.Equal(BaseTime.AddMinutes(5), updated.GetProperty("updatedAt").GetDateTimeOffset());
        Assert.NotEqual(created.GetProperty("rowVersion").GetString(), updated.GetProperty("rowVersion").GetString());
    }

    [Fact]
    public async Task A_stale_row_version_is_409_and_an_unknown_id_is_404()
    {
        var token = await PatientManagerAsync(_fixture, _client);
        var created = await CreatePatientOkAsync(_client, token);
        var id = created.GetProperty("id").GetInt64();
        var original = created.GetProperty("rowVersion").GetString();
        var phone = created.GetProperty("phone").GetString();

        Assert.Equal(HttpStatusCode.OK, (await UpdatePatientAsync(_client, token, id, "A", phone, original)).StatusCode);

        await AssertProblemAsync(await UpdatePatientAsync(_client, token, id, "B", phone, original), HttpStatusCode.Conflict, "error.concurrency.conflict");
        await AssertProblemAsync(await UpdatePatientAsync(_client, token, 987654321, "B", phone, original), HttpStatusCode.NotFound, "error.patient.not_found");
    }

    [Fact]
    public async Task Delete_is_a_soft_delete_that_hides_the_patient()
    {
        var (token, userId) = await SignInAsync(_fixture, _client, AllPatientPermissions);
        var created = await CreatePatientOkAsync(_client, token);
        var id = created.GetProperty("id").GetInt64();

        Assert.Equal(HttpStatusCode.NoContent, (await _client.SendAsync(Json(HttpMethod.Delete, $"/api/patients/{id}", token))).StatusCode);

        await AssertProblemAsync(await _client.SendAsync(Json(HttpMethod.Get, $"/api/patients/{id}", token)), HttpStatusCode.NotFound, "error.patient.not_found");
        await AssertProblemAsync(await _client.SendAsync(Json(HttpMethod.Delete, $"/api/patients/{id}", token)), HttpStatusCode.NotFound, "error.patient.not_found");
        Assert.DoesNotContain(id, PatientIds(await ListPatientsAsync(_client, token, $"search={Uri.EscapeDataString(created.GetProperty("phone").GetString()!)}")));

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var row = await db.Patients.IgnoreQueryFilters().AsNoTracking().SingleAsync(p => p.Id == id);
        Assert.True(row.IsDeleted);
        Assert.Equal(userId, row.DeletedBy);
    }

    [Fact]
    public async Task Audit_fields_record_the_real_user_of_each_step()
    {
        var (creator, creatorId) = await SignInAsync(_fixture, _client, AllPatientPermissions);
        var (editor, editorId) = await SignInAsync(_fixture, _client, AllPatientPermissions);
        var created = await CreatePatientOkAsync(_client, creator);
        var id = created.GetProperty("id").GetInt64();
        _fixture.Clock.Now = BaseTime.AddMinutes(1);

        Assert.Equal(HttpStatusCode.OK, (await UpdatePatientAsync(_client, editor, id, "X", created.GetProperty("phone").GetString(), created.GetProperty("rowVersion").GetString())).StatusCode);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var row = await db.Patients.AsNoTracking().SingleAsync(p => p.Id == id);
        Assert.Equal(creatorId, row.CreatedBy);
        Assert.Equal(editorId, row.UpdatedBy);
        Assert.Equal(BaseTime.AddMinutes(1), row.UpdatedAt);
    }
}
