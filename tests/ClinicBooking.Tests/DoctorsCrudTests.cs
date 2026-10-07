using System.Net;
using System.Text.Json;
using ClinicBooking.Application.Interfaces;
using ClinicBooking.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using static ClinicBooking.Tests.Support.AuthHelpers;
using static ClinicBooking.Tests.Support.DoctorHelpers;
using static ClinicBooking.Tests.Support.SpecialtyHelpers;

namespace ClinicBooking.Tests;

/// <summary>Doctors: validation, create, read, full-replace update, soft delete, audit, in-use conflicts (D61).</summary>
[Collection(SqlServerCollection.Name)]
public class DoctorsCrudTests : IClassFixture<AuthApiFixture>
{
    // 09:00 UTC is 12:00 in Cairo (summer time), the same calendar day.
    private static readonly DateTimeOffset BaseTime = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly AuthApiFixture _fixture;
    private readonly HttpClient _client;

    public DoctorsCrudTests(AuthApiFixture fixture)
    {
        _fixture = fixture;
        _fixture.Clock.Now = BaseTime;
        _client = fixture.Factory.CreateClient();
    }

    private sealed record Setup(long Clinic, long Specialty, string Token, long UserId);

    private async Task<Setup> SetupAsync()
    {
        var clinic = await NewClinicAsync(_fixture, _client);
        var specialty = await NewSpecialtyAsync(_fixture, _client);
        var (token, userId) = await DoctorManagerAsync(_fixture, _client, clinic);
        return new Setup(clinic, specialty, token, userId);
    }

    // ---- validation -------------------------------------------------------------------------

    public static TheoryData<string?, string?, string, string> InvalidNames => new()
    {
        { "", "Valid", "nameAr", "error.doctor.name_ar_required" },
        { null, "Valid", "nameAr", "error.doctor.name_ar_required" },
        { "صحيح", "  ", "nameEn", "error.doctor.name_en_required" },
        { new string('ب', 101), "Valid", "nameAr", "error.doctor.name_ar_too_long" },
        { "صحيح", new string('a', 101), "nameEn", "error.doctor.name_en_too_long" },
        { "ـً", "Valid", "nameAr", "error.doctor.name_ar_invalid" }
    };

    [Theory]
    [MemberData(nameof(InvalidNames))]
    public async Task Create_rejects_invalid_names_with_keys(string? nameAr, string? nameEn, string field, string key)
    {
        var s = await SetupAsync();

        var response = await SendAsync(_client, HttpMethod.Post, "/api/doctors", s.Token, new
        {
            nameAr, nameEn, specialtyIds = new[] { s.Specialty }, clinicIds = new[] { s.Clinic }, slotMinutes = 15
        });

        Assert.Equal(key, await FieldErrorAsync(response, field));
    }

    [Fact]
    public async Task Create_requires_specialties_clinics_and_a_slot_duration()
    {
        var s = await SetupAsync();

        var response = await SendAsync(_client, HttpMethod.Post, "/api/doctors", s.Token, new
        {
            nameAr = UniqueArabic(), nameEn = UniqueEnglish(), specialtyIds = Array.Empty<long>()
        });

        var errors = (await ProblemAsync(response)).GetProperty("errors");
        Assert.Equal("error.doctor.specialties_required", errors.GetProperty("specialtyIds")[0].GetString());
        Assert.Equal("error.doctor.clinics_required", errors.GetProperty("clinicIds")[0].GetString());
        Assert.Equal("error.doctor.slot_minutes_required", errors.GetProperty("slotMinutes")[0].GetString());
    }

    [Fact]
    public async Task Create_limits_the_number_of_distinct_specialties_and_clinics()
    {
        var s = await SetupAsync();

        var response = await CreateDoctorAsync(
            _client, s.Token, UniqueArabic(), UniqueEnglish(),
            Enumerable.Range(1, 11).Select(i => (long)i).ToArray(),
            Enumerable.Range(1, 21).Select(i => (long)i).ToArray());

        var errors = (await ProblemAsync(response)).GetProperty("errors");
        Assert.Equal("error.doctor.specialties_too_many", errors.GetProperty("specialtyIds")[0].GetString());
        Assert.Equal("error.doctor.clinics_too_many", errors.GetProperty("clinicIds")[0].GetString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(7)]
    [InlineData(125)]
    [InlineData(-5)]
    public async Task Create_rejects_a_slot_duration_outside_5_to_120_in_steps_of_5(int minutes)
    {
        var s = await SetupAsync();

        var response = await CreateDoctorAsync(_client, s.Token, UniqueArabic(), UniqueEnglish(), [s.Specialty], [s.Clinic], minutes);

        Assert.Equal("error.doctor.slot_minutes_invalid", await FieldErrorAsync(response, "slotMinutes"));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(120)]
    public async Task Create_accepts_the_slot_duration_limits(int minutes)
    {
        var s = await SetupAsync();

        var doctor = await CreateDoctorOkAsync(_client, s.Token, [s.Specialty], [s.Clinic], slotMinutes: minutes);

        Assert.Equal(minutes, doctor.GetProperty("slotMinutes").GetInt32());
    }

    [Theory]
    [InlineData(null, "error.concurrency.row_version_required")]
    [InlineData("AAAA", "error.concurrency.row_version_invalid")]
    public async Task Update_requires_a_valid_row_version(string? rowVersion, string key)
    {
        var s = await SetupAsync();
        var doctor = await CreateDoctorOkAsync(_client, s.Token, [s.Specialty], [s.Clinic]);

        var response = await UpdateDoctorAsync(_client, s.Token, doctor.GetProperty("id").GetInt64(), UniqueArabic(), UniqueEnglish(), [s.Specialty], rowVersion);

        Assert.Equal(key, await FieldErrorAsync(response, "rowVersion"));
    }

    [Theory]
    [InlineData("page=0", "page", "error.paging.page_invalid")]
    [InlineData("pageSize=101", "pageSize", "error.paging.page_size_invalid")]
    [InlineData("sortBy=specialty", "sortBy", "error.sort.invalid")]
    [InlineData("sortDirection=up", "sortDirection", "error.sort.invalid")]
    [InlineData("isActive=true", "isActive", "error.doctor.is_active_requires_clinic")]
    public async Task List_rejects_invalid_parameters_with_keys(string query, string field, string key)
    {
        var (token, _) = await SignInAsync(_fixture, _client);

        var response = await SendAsync(_client, HttpMethod.Get, $"/api/doctors?{query}", token);

        Assert.Equal(key, await FieldErrorAsync(response, field));
    }

    [Fact]
    public async Task An_unknown_or_deleted_specialty_is_a_400_on_the_field()
    {
        var s = await SetupAsync();
        var deleted = await NewSpecialtyAsync(_fixture, _client);
        var specialtyManager = await ManagerTokenAsync(_fixture, _client);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(_client, HttpMethod.Delete, $"/api/specialties/{deleted}", specialtyManager)).StatusCode);

        var unknown = await CreateDoctorAsync(_client, s.Token, UniqueArabic(), UniqueEnglish(), [s.Specialty, 987654321], [s.Clinic]);
        var gone = await CreateDoctorAsync(_client, s.Token, UniqueArabic(), UniqueEnglish(), [deleted], [s.Clinic]);

        Assert.Equal("error.doctor.specialty_unavailable", await FieldErrorAsync(unknown, "specialtyIds"));
        Assert.Equal("error.doctor.specialty_unavailable", await FieldErrorAsync(gone, "specialtyIds"));
    }

    // A value that slips past the validator (a caller other than the controller) is a 400 key, never a 500 (D55).
    [Theory]
    [InlineData(7)]
    [InlineData(0)]
    [InlineData(125)]
    public void A_slot_duration_that_bypasses_the_validator_is_an_InvalidRequestException(int minutes)
    {
        var failure = Assert.Throws<ClinicBooking.Domain.Exceptions.InvalidRequestException>(
            () => ClinicBooking.Domain.Entities.DoctorSlotDuration.Create(1, minutes, new DateOnly(2026, 7, 2)));

        Assert.Equal("error.doctor.slot_minutes_invalid", failure.ErrorKey);
    }

    // ---- create, read, update, delete -------------------------------------------------------

    [Fact]
    public async Task Create_returns_201_with_a_location_and_the_full_doctor()
    {
        var s = await SetupAsync();
        var second = await NewSpecialtyAsync(_fixture, _client);
        var nameAr = UniqueArabic();
        var nameEn = UniqueEnglish();

        var response = await CreateDoctorAsync(
            _client, s.Token, $"  {nameAr} ", $" {nameEn}  ", [s.Specialty, second, s.Specialty], [s.Clinic, s.Clinic], 20);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ProblemAsync(response);
        var id = body.GetProperty("id").GetInt64();
        Assert.Equal($"/api/doctors/{id}", response.Headers.Location!.AbsolutePath);
        Assert.Equal(nameAr, body.GetProperty("nameAr").GetString());
        Assert.Equal(nameEn, body.GetProperty("nameEn").GetString());
        Assert.Equal(new[] { s.Specialty, second }.Order().ToArray(), SpecialtyIds(body)); // duplicates collapsed
        Assert.Equal([(s.Clinic, true)], Assignments(body)); // new assignments start active
        var clinic = body.GetProperty("clinics")[0];
        Assert.False(string.IsNullOrEmpty(clinic.GetProperty("nameAr").GetString()));
        Assert.False(string.IsNullOrEmpty(body.GetProperty("specialties")[0].GetProperty("nameEn").GetString()));
        Assert.Equal(20, body.GetProperty("slotMinutes").GetInt32());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("pendingSlotChange").ValueKind);
        Assert.Equal(BaseTime, body.GetProperty("createdAt").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("updatedAt").ValueKind);
        Assert.Equal(12, body.GetProperty("rowVersion").GetString()!.Length);
        Assert.False(body.TryGetProperty("nameArNormalized", out _));
        Assert.False(body.TryGetProperty("isDeleted", out _));
        Assert.False(body.TryGetProperty("createdBy", out _));
    }

    [Fact]
    public async Task The_first_slot_duration_takes_effect_on_the_Cairo_creation_day()
    {
        // 22:30 UTC on 1 July is 01:30 on 2 July in Cairo.
        _fixture.Clock.Now = new DateTimeOffset(2026, 7, 1, 22, 30, 0, TimeSpan.Zero);
        var s = await SetupAsync(); // signed in after the clock moved, so the token is valid
        var doctor = await CreateDoctorOkAsync(_client, s.Token, [s.Specialty], [s.Clinic]);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var row = await db.DoctorSlotDurations.AsNoTracking().SingleAsync(d => d.DoctorId == doctor.GetProperty("id").GetInt64());
        Assert.Equal(new DateOnly(2026, 7, 2), row.EffectiveFrom);
        Assert.Equal(15, row.SlotMinutes);
    }

    [Fact]
    public async Task Doctors_may_share_names()
    {
        var s = await SetupAsync();
        var nameAr = UniqueArabic();
        var nameEn = UniqueEnglish();

        await CreateDoctorOkAsync(_client, s.Token, [s.Specialty], [s.Clinic], nameAr, nameEn);
        var again = await CreateDoctorAsync(_client, s.Token, nameAr, nameEn, [s.Specialty], [s.Clinic]);

        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
    }

    [Fact]
    public async Task Get_returns_the_doctor_and_404_for_an_unknown_id()
    {
        var s = await SetupAsync();
        var doctor = await CreateDoctorOkAsync(_client, s.Token, [s.Specialty], [s.Clinic]);

        var found = await GetDoctorAsync(_client, s.Token, doctor.GetProperty("id").GetInt64());
        var missing = await SendAsync(_client, HttpMethod.Get, "/api/doctors/987654321", s.Token);

        Assert.Equal(doctor.GetProperty("rowVersion").GetString(), found.GetProperty("rowVersion").GetString());
        await AssertProblemAsync(missing, HttpStatusCode.NotFound, "error.doctor.not_found");
    }

    [Fact]
    public async Task Update_replaces_names_and_the_specialty_set()
    {
        var s = await SetupAsync();
        var kept = await NewSpecialtyAsync(_fixture, _client);
        var added = await NewSpecialtyAsync(_fixture, _client);
        var doctor = await CreateDoctorOkAsync(_client, s.Token, [s.Specialty, kept], [s.Clinic]);
        var id = doctor.GetProperty("id").GetInt64();
        var newAr = UniqueArabic();
        var newEn = UniqueEnglish();
        _fixture.Clock.Now = BaseTime.AddMinutes(5);

        var response = await UpdateDoctorAsync(_client, s.Token, id, newAr, newEn, [kept, added], doctor.GetProperty("rowVersion").GetString());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await ProblemAsync(response);
        Assert.Equal(newAr, updated.GetProperty("nameAr").GetString());
        Assert.Equal(newEn, updated.GetProperty("nameEn").GetString());
        Assert.Equal(new[] { kept, added }.Order().ToArray(), SpecialtyIds(updated));
        Assert.Equal([(s.Clinic, true)], Assignments(updated)); // clinics are not part of the PUT
        Assert.Equal(BaseTime.AddMinutes(5), updated.GetProperty("updatedAt").GetDateTimeOffset());
        Assert.NotEqual(doctor.GetProperty("rowVersion").GetString(), updated.GetProperty("rowVersion").GetString());

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        Assert.Equal(2, await db.DoctorSpecialties.CountAsync(d => d.DoctorId == id)); // the removed row is gone
    }

    [Fact]
    public async Task A_change_to_the_specialties_alone_moves_the_row_version()
    {
        var s = await SetupAsync();
        var other = await NewSpecialtyAsync(_fixture, _client);
        var doctor = await CreateDoctorOkAsync(_client, s.Token, [s.Specialty], [s.Clinic]);
        var id = doctor.GetProperty("id").GetInt64();
        var original = doctor.GetProperty("rowVersion").GetString();

        var first = await UpdateDoctorAsync(_client, s.Token, id, doctor.GetProperty("nameAr").GetString()!, doctor.GetProperty("nameEn").GetString()!, [other], original);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.NotEqual(original, (await ProblemAsync(first)).GetProperty("rowVersion").GetString());

        // Someone still holding the original version must reload, even though the names never changed.
        var stale = await UpdateDoctorAsync(_client, s.Token, id, doctor.GetProperty("nameAr").GetString()!, doctor.GetProperty("nameEn").GetString()!, [s.Specialty], original);
        await AssertProblemAsync(stale, HttpStatusCode.Conflict, "error.concurrency.conflict");
    }

    [Fact]
    public async Task Update_rejects_an_unavailable_specialty_and_changes_nothing()
    {
        var s = await SetupAsync();
        var doctor = await CreateDoctorOkAsync(_client, s.Token, [s.Specialty], [s.Clinic]);
        var id = doctor.GetProperty("id").GetInt64();

        var response = await UpdateDoctorAsync(_client, s.Token, id, UniqueArabic(), UniqueEnglish(), [987654321], doctor.GetProperty("rowVersion").GetString());

        Assert.Equal("error.doctor.specialty_unavailable", await FieldErrorAsync(response, "specialtyIds"));
        var current = await GetDoctorAsync(_client, s.Token, id);
        Assert.Equal(doctor.GetProperty("nameAr").GetString(), current.GetProperty("nameAr").GetString());
        Assert.Equal([s.Specialty], SpecialtyIds(current));
    }

    [Fact]
    public async Task Update_of_an_unknown_id_is_404()
    {
        var s = await SetupAsync();

        var response = await UpdateDoctorAsync(_client, s.Token, 987654321, UniqueArabic(), UniqueEnglish(), [s.Specialty], "AAAAAAAAAAA=");

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "error.doctor.not_found");
    }

    [Fact]
    public async Task A_stale_row_version_is_a_409_and_the_current_one_succeeds()
    {
        var s = await SetupAsync();
        var doctor = await CreateDoctorOkAsync(_client, s.Token, [s.Specialty], [s.Clinic]);
        var id = doctor.GetProperty("id").GetInt64();
        var original = doctor.GetProperty("rowVersion").GetString();

        var first = await UpdateDoctorAsync(_client, s.Token, id, UniqueArabic(), UniqueEnglish(), [s.Specialty], original);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var stale = await UpdateDoctorAsync(_client, s.Token, id, UniqueArabic(), UniqueEnglish(), [s.Specialty], original);
        await AssertProblemAsync(stale, HttpStatusCode.Conflict, "error.concurrency.conflict");

        var retry = await UpdateDoctorAsync(_client, s.Token, id, UniqueArabic(), UniqueEnglish(), [s.Specialty], (await ProblemAsync(first)).GetProperty("rowVersion").GetString());
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    [Fact]
    public async Task Delete_is_a_soft_delete_that_hides_the_doctor_and_keeps_its_rows()
    {
        var s = await SetupAsync();
        var nameAr = UniqueArabic();
        var doctor = await CreateDoctorOkAsync(_client, s.Token, [s.Specialty], [s.Clinic], nameAr);
        var id = doctor.GetProperty("id").GetInt64();

        var deleted = await SendAsync(_client, HttpMethod.Delete, $"/api/doctors/{id}", s.Token);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        await AssertProblemAsync(await SendAsync(_client, HttpMethod.Get, $"/api/doctors/{id}", s.Token), HttpStatusCode.NotFound, "error.doctor.not_found");
        Assert.Empty(Ids(await ListDoctorsAsync(_client, s.Token, $"search={Uri.EscapeDataString(nameAr)}")));
        await AssertProblemAsync(await SendAsync(_client, HttpMethod.Delete, $"/api/doctors/{id}", s.Token), HttpStatusCode.NotFound, "error.doctor.not_found");
        await AssertProblemAsync(await UpdateDoctorAsync(_client, s.Token, doctor), HttpStatusCode.NotFound, "error.doctor.not_found");

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var row = await db.Doctors.IgnoreQueryFilters().AsNoTracking().SingleAsync(d => d.Id == id);
        Assert.True(row.IsDeleted);
        Assert.Equal(s.UserId, row.DeletedBy);
        Assert.Equal(1, await db.DoctorSpecialties.CountAsync(d => d.DoctorId == id));
        Assert.Equal(1, await db.DoctorClinics.CountAsync(d => d.DoctorId == id));
        Assert.Equal(1, await db.DoctorSlotDurations.CountAsync(d => d.DoctorId == id));
    }

    [Fact]
    public async Task Audit_fields_record_the_real_user_of_each_step()
    {
        var clinic = await NewClinicAsync(_fixture, _client);
        var specialty = await NewSpecialtyAsync(_fixture, _client);
        var (creator, creatorId) = await DoctorManagerAsync(_fixture, _client, clinic);
        var (editor, editorId) = await DoctorManagerAsync(_fixture, _client, clinic);
        var (remover, removerId) = await DoctorManagerAsync(_fixture, _client, clinic);

        var doctor = await CreateDoctorOkAsync(_client, creator, [specialty], [clinic]);
        var id = doctor.GetProperty("id").GetInt64();
        _fixture.Clock.Now = BaseTime.AddMinutes(1);
        Assert.Equal(HttpStatusCode.OK, (await UpdateDoctorAsync(_client, editor, doctor)).StatusCode);
        _fixture.Clock.Now = BaseTime.AddMinutes(2);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(_client, HttpMethod.Delete, $"/api/doctors/{id}", remover)).StatusCode);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var row = await db.Doctors.IgnoreQueryFilters().AsNoTracking().SingleAsync(d => d.Id == id);
        Assert.Equal(creatorId, row.CreatedBy);
        Assert.Equal(BaseTime, row.CreatedAt);
        Assert.Equal(removerId, row.UpdatedBy);
        Assert.Equal(removerId, row.DeletedBy);
        Assert.Equal(BaseTime.AddMinutes(2), row.DeletedAt);
        var assignment = await db.DoctorClinics.AsNoTracking().SingleAsync(d => d.DoctorId == id);
        Assert.Equal(creatorId, assignment.CreatedBy);
        Assert.NotEqual(editorId, creatorId);
    }

    // ---- in use -----------------------------------------------------------------------------

    [Fact]
    public async Task A_specialty_used_by_a_live_doctor_cannot_be_deleted_but_a_deleted_doctor_does_not_count()
    {
        var s = await SetupAsync();
        var specialtyManager = await ManagerTokenAsync(_fixture, _client);
        var doctor = await CreateDoctorOkAsync(_client, s.Token, [s.Specialty], [s.Clinic]);

        var blocked = await SendAsync(_client, HttpMethod.Delete, $"/api/specialties/{s.Specialty}", specialtyManager);
        await AssertProblemAsync(blocked, HttpStatusCode.Conflict, "error.specialty.in_use");

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(_client, HttpMethod.Delete, $"/api/doctors/{doctor.GetProperty("id").GetInt64()}", s.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(_client, HttpMethod.Delete, $"/api/specialties/{s.Specialty}", specialtyManager)).StatusCode);
    }

    [Fact]
    public async Task A_clinic_with_a_live_doctors_assignment_cannot_be_deleted_but_a_deleted_doctor_does_not_count()
    {
        var s = await SetupAsync();
        var clinicManager = await ClinicHelpers.ClinicManagerTokenAsync(_fixture, _client);
        var doctor = await CreateDoctorOkAsync(_client, s.Token, [s.Specialty], [s.Clinic]);

        var blocked = await SendAsync(_client, HttpMethod.Delete, $"/api/clinics/{s.Clinic}", clinicManager);
        await AssertProblemAsync(blocked, HttpStatusCode.Conflict, "error.clinic.in_use");

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(_client, HttpMethod.Delete, $"/api/doctors/{doctor.GetProperty("id").GetInt64()}", s.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(_client, HttpMethod.Delete, $"/api/clinics/{s.Clinic}", clinicManager)).StatusCode);
    }

    [Fact]
    public async Task An_unused_specialty_and_clinic_can_still_be_deleted()
    {
        var specialty = await NewSpecialtyAsync(_fixture, _client);
        var clinic = await NewClinicAsync(_fixture, _client);

        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(_client, HttpMethod.Delete, $"/api/specialties/{specialty}", await ManagerTokenAsync(_fixture, _client))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await SendAsync(_client, HttpMethod.Delete, $"/api/clinics/{clinic}", await ClinicHelpers.ClinicManagerTokenAsync(_fixture, _client))).StatusCode);
    }
}
