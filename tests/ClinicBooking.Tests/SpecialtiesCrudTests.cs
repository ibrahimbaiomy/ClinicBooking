using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ClinicBooking.Application.Interfaces;
using ClinicBooking.Domain.Permissions;
using ClinicBooking.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using static ClinicBooking.Tests.Support.AuthHelpers;
using static ClinicBooking.Tests.Support.SpecialtyHelpers;

namespace ClinicBooking.Tests;

[Collection(SqlServerCollection.Name)]
public class SpecialtiesCrudTests : IClassFixture<AuthApiFixture>
{
    private static readonly DateTimeOffset BaseTime = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly AuthApiFixture _fixture;
    private readonly HttpClient _client;

    public SpecialtiesCrudTests(AuthApiFixture fixture)
    {
        _fixture = fixture;
        _fixture.Clock.Now = BaseTime;
        _client = fixture.Factory.CreateClient();
    }

    // ---- validation -------------------------------------------------------------------------

    public static TheoryData<string?, string?, string, string> InvalidBodies => new()
    {
        { "", "Valid", "nameAr", "error.specialty.name_ar_required" },
        { "   ", "Valid", "nameAr", "error.specialty.name_ar_required" },
        { null, "Valid", "nameAr", "error.specialty.name_ar_required" },
        { "صحيح", "", "nameEn", "error.specialty.name_en_required" },
        { "صحيح", null, "nameEn", "error.specialty.name_en_required" },
        { new string('ب', 101), "Valid", "nameAr", "error.specialty.name_ar_too_long" },
        { "صحيح", new string('a', 101), "nameEn", "error.specialty.name_en_too_long" },
        { "ـً", "Valid", "nameAr", "error.specialty.name_ar_invalid" }
    };

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task Create_rejects_invalid_names_with_keys(string? nameAr, string? nameEn, string field, string key)
    {
        var token = await ManagerTokenAsync(_fixture, _client);

        var response = await _client.SendAsync(Json(HttpMethod.Post, "/api/specialties", token, new { nameAr, nameEn }));

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "error.validation.failed");
        var errors = (await ProblemAsync(response)).GetProperty("errors");
        Assert.Equal(key, errors.GetProperty(field)[0].GetString());
    }

    [Fact]
    public async Task Create_reports_every_failing_field_at_once()
    {
        var token = await ManagerTokenAsync(_fixture, _client);

        var response = await _client.SendAsync(Json(HttpMethod.Post, "/api/specialties", token, new { }));

        var errors = (await ProblemAsync(response)).GetProperty("errors");
        Assert.Equal("error.specialty.name_ar_required", errors.GetProperty("nameAr")[0].GetString());
        Assert.Equal("error.specialty.name_en_required", errors.GetProperty("nameEn")[0].GetString());
    }

    [Fact]
    public async Task A_malformed_body_is_a_400_with_keys()
    {
        var token = await ManagerTokenAsync(_fixture, _client);
        var request = WithBearer(HttpMethod.Post, "/api/specialties", token);
        request.Content = new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json");

        var response = await _client.SendAsync(request);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "error.validation.failed");
    }

    [Theory]
    [InlineData(null, "error.concurrency.row_version_required")]
    [InlineData("", "error.concurrency.row_version_required")]
    [InlineData("not base64!", "error.concurrency.row_version_invalid")]
    [InlineData("AAAA", "error.concurrency.row_version_invalid")]
    public async Task Update_requires_a_valid_row_version(string? rowVersion, string key)
    {
        var token = await ManagerTokenAsync(_fixture, _client);
        var created = await CreateOkAsync(_client, token, UniqueArabic(), UniqueEnglish());

        var response = await UpdateAsync(_client, token, created.GetProperty("id").GetInt64(), UniqueArabic(), UniqueEnglish(), rowVersion);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "error.validation.failed");
        Assert.Equal(key, (await ProblemAsync(response)).GetProperty("errors").GetProperty("rowVersion")[0].GetString());
    }

    [Theory]
    [InlineData("page=0", "page", "error.paging.page_invalid")]
    [InlineData("pageSize=0", "pageSize", "error.paging.page_size_invalid")]
    [InlineData("pageSize=101", "pageSize", "error.paging.page_size_invalid")]
    [InlineData("sortBy=password", "sortBy", "error.sort.invalid")]
    [InlineData("sortDirection=sideways", "sortDirection", "error.sort.invalid")]
    public async Task List_rejects_invalid_parameters_with_keys(string query, string field, string key)
    {
        var token = await ManagerTokenAsync(_fixture, _client);

        var response = await _client.SendAsync(Json(HttpMethod.Get, $"/api/specialties?{query}", token));

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "error.validation.failed");
        Assert.Equal(key, (await ProblemAsync(response)).GetProperty("errors").GetProperty(field)[0].GetString());
    }

    [Fact]
    public async Task List_rejects_a_search_that_is_too_long()
    {
        var token = await ManagerTokenAsync(_fixture, _client);

        var response = await _client.SendAsync(Json(HttpMethod.Get, $"/api/specialties?search={new string('a', 101)}", token));

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "error.validation.failed");
        Assert.Equal("error.search.too_long", (await ProblemAsync(response)).GetProperty("errors").GetProperty("search")[0].GetString());
    }

    [Fact]
    public async Task A_non_numeric_page_is_a_400_not_a_500()
    {
        var token = await ManagerTokenAsync(_fixture, _client);

        var response = await _client.SendAsync(Json(HttpMethod.Get, "/api/specialties?page=abc", token));

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "error.validation.failed");
    }

    // ---- create, read, update, delete -------------------------------------------------------

    [Fact]
    public async Task Create_returns_201_with_a_location_and_the_trimmed_names()
    {
        var token = await ManagerTokenAsync(_fixture, _client);
        var nameAr = UniqueArabic();
        var nameEn = UniqueEnglish();

        var response = await CreateAsync(_client, token, $"  {nameAr}  ", $" {nameEn} ");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ProblemAsync(response);
        var id = body.GetProperty("id").GetInt64();
        Assert.Equal($"/api/specialties/{id}", response.Headers.Location!.AbsolutePath);
        Assert.Equal(nameAr, body.GetProperty("nameAr").GetString());
        Assert.Equal(nameEn, body.GetProperty("nameEn").GetString());
        Assert.Equal(BaseTime, body.GetProperty("createdAt").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("updatedAt").ValueKind);
        Assert.Equal(12, body.GetProperty("rowVersion").GetString()!.Length); // 8 bytes in base64
        Assert.False(body.TryGetProperty("nameArNormalized", out _));
        Assert.False(body.TryGetProperty("isDeleted", out _));
    }

    [Fact]
    public async Task Get_returns_the_specialty_and_404_for_an_unknown_id()
    {
        var token = await ManagerTokenAsync(_fixture, _client);
        var created = await CreateOkAsync(_client, token, UniqueArabic(), UniqueEnglish());

        var found = await _client.SendAsync(Json(HttpMethod.Get, $"/api/specialties/{created.GetProperty("id").GetInt64()}", token));
        var missing = await _client.SendAsync(Json(HttpMethod.Get, "/api/specialties/987654321", token));

        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        Assert.Equal(created.GetProperty("nameAr").GetString(), (await ProblemAsync(found)).GetProperty("nameAr").GetString());
        await AssertProblemAsync(missing, HttpStatusCode.NotFound, "error.specialty.not_found");
    }

    [Fact]
    public async Task Update_changes_the_names_and_the_row_version()
    {
        var token = await ManagerTokenAsync(_fixture, _client);
        var created = await CreateOkAsync(_client, token, UniqueArabic(), UniqueEnglish());
        var id = created.GetProperty("id").GetInt64();
        var newAr = UniqueArabic();
        var newEn = UniqueEnglish();

        var later = BaseTime.AddMinutes(5);
        _fixture.Clock.Now = later;
        var response = await UpdateAsync(_client, token, id, newAr, newEn, created.GetProperty("rowVersion").GetString());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await ProblemAsync(response);
        Assert.Equal(newAr, updated.GetProperty("nameAr").GetString());
        Assert.Equal(newEn, updated.GetProperty("nameEn").GetString());
        Assert.Equal(BaseTime, updated.GetProperty("createdAt").GetDateTimeOffset());
        Assert.Equal(later, updated.GetProperty("updatedAt").GetDateTimeOffset());
        Assert.NotEqual(created.GetProperty("rowVersion").GetString(), updated.GetProperty("rowVersion").GetString());
    }

    [Fact]
    public async Task Update_of_an_unknown_id_is_404()
    {
        var token = await ManagerTokenAsync(_fixture, _client);

        var response = await UpdateAsync(_client, token, 987654321, UniqueArabic(), UniqueEnglish(), "AAAAAAAAAAA=");

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "error.specialty.not_found");
    }

    [Fact]
    public async Task A_stale_row_version_is_a_409_and_the_current_one_succeeds()
    {
        var token = await ManagerTokenAsync(_fixture, _client);
        var created = await CreateOkAsync(_client, token, UniqueArabic(), UniqueEnglish());
        var id = created.GetProperty("id").GetInt64();
        var original = created.GetProperty("rowVersion").GetString();

        var first = await UpdateAsync(_client, token, id, UniqueArabic(), UniqueEnglish(), original);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var stale = await UpdateAsync(_client, token, id, UniqueArabic(), UniqueEnglish(), original);
        await AssertProblemAsync(stale, HttpStatusCode.Conflict, "error.concurrency.conflict");

        var current = (await ProblemAsync(first)).GetProperty("rowVersion").GetString();
        var retry = await UpdateAsync(_client, token, id, UniqueArabic(), UniqueEnglish(), current);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    [Fact]
    public async Task Delete_is_a_soft_delete_that_hides_the_row_everywhere()
    {
        var (token, userId) = await SignInAsync(_fixture, _client, Permissions.Specialties.Manage);
        var nameAr = UniqueArabic();
        var created = await CreateOkAsync(_client, token, nameAr, UniqueEnglish());
        var id = created.GetProperty("id").GetInt64();

        var deleted = await _client.SendAsync(Json(HttpMethod.Delete, $"/api/specialties/{id}", token));

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var get = await _client.SendAsync(Json(HttpMethod.Get, $"/api/specialties/{id}", token));
        await AssertProblemAsync(get, HttpStatusCode.NotFound, "error.specialty.not_found");
        Assert.Empty((await ListAsync(_client, token, $"search={Uri.EscapeDataString(nameAr)}")).GetProperty("items").EnumerateArray());
        var again = await _client.SendAsync(Json(HttpMethod.Delete, $"/api/specialties/{id}", token));
        await AssertProblemAsync(again, HttpStatusCode.NotFound, "error.specialty.not_found");

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var row = await db.Specialties.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.Id == id);
        Assert.True(row.IsDeleted);
        Assert.Equal(userId, row.DeletedBy);
    }

    [Fact]
    public async Task Audit_fields_record_the_real_user_of_each_step()
    {
        var (creator, creatorId) = await SignInAsync(_fixture, _client, Permissions.Specialties.Manage);
        var (editor, editorId) = await SignInAsync(_fixture, _client, Permissions.Specialties.Manage);
        var (remover, removerId) = await SignInAsync(_fixture, _client, Permissions.Specialties.Manage);

        var created = await CreateOkAsync(_client, creator, UniqueArabic(), UniqueEnglish());
        var id = created.GetProperty("id").GetInt64();
        _fixture.Clock.Now = BaseTime.AddMinutes(1);
        await UpdateAsync(_client, editor, id, UniqueArabic(), UniqueEnglish(), created.GetProperty("rowVersion").GetString());
        _fixture.Clock.Now = BaseTime.AddMinutes(2);
        await _client.SendAsync(Json(HttpMethod.Delete, $"/api/specialties/{id}", remover));

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var row = await db.Specialties.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.Id == id);
        Assert.Equal(creatorId, row.CreatedBy);
        Assert.Equal(BaseTime, row.CreatedAt);
        Assert.Equal(removerId, row.UpdatedBy); // the delete is the last update
        Assert.Equal(removerId, row.DeletedBy);
        Assert.Equal(BaseTime.AddMinutes(2), row.DeletedAt);
        Assert.NotEqual(editorId, row.CreatedBy);
    }

    // ---- duplicates -------------------------------------------------------------------------

    [Fact]
    public async Task Exact_duplicates_are_409_with_the_matching_key()
    {
        var token = await ManagerTokenAsync(_fixture, _client);
        var nameAr = UniqueArabic();
        var nameEn = UniqueEnglish();
        await CreateOkAsync(_client, token, nameAr, nameEn);

        var sameAr = await CreateAsync(_client, token, nameAr, UniqueEnglish());
        var sameEn = await CreateAsync(_client, token, UniqueArabic(), nameEn);

        await AssertProblemAsync(sameAr, HttpStatusCode.Conflict, "error.specialty.name_ar_taken");
        await AssertProblemAsync(sameEn, HttpStatusCode.Conflict, "error.specialty.name_en_taken");
    }

    public static TheoryData<string, string> ArabicVariants => new()
    {
        { "أحمد", "احمد" },
        { "أحمد", "إحمد" },
        { "آمال", "امال" },
        { "مدرسة", "مدرسه" },
        { "مستشفى", "مستشفي" },
        { "قَلْب", "قلب" },
        { "قلـــب", "قلب" },
        { "عيادة ١٢", "عياده 12" }
    };

    [Theory]
    [MemberData(nameof(ArabicVariants))]
    public async Task Differently_spelled_Arabic_names_count_as_the_same_name(string original, string variant)
    {
        var token = await ManagerTokenAsync(_fixture, _client);
        var unique = UniqueArabic();
        await CreateOkAsync(_client, token, $"{original} {unique}", UniqueEnglish());

        var duplicate = await CreateAsync(_client, token, $"{variant} {unique}", UniqueEnglish());

        await AssertProblemAsync(duplicate, HttpStatusCode.Conflict, "error.specialty.name_ar_taken");
    }

    [Fact]
    public async Task English_names_differing_only_in_case_or_spacing_are_duplicates()
    {
        var token = await ManagerTokenAsync(_fixture, _client);
        var unique = UniqueEnglish();
        await CreateOkAsync(_client, token, UniqueArabic(), $"Pediatric Surgery {unique}");

        var duplicate = await CreateAsync(_client, token, UniqueArabic(), $"  pediatric   SURGERY {unique} ");

        await AssertProblemAsync(duplicate, HttpStatusCode.Conflict, "error.specialty.name_en_taken");
    }

    [Fact]
    public async Task Editing_into_another_names_is_409_but_keeping_or_respelling_your_own_is_fine()
    {
        var token = await ManagerTokenAsync(_fixture, _client);
        var unique = UniqueArabic();
        var other = await CreateOkAsync(_client, token, $"أحمد {unique}", UniqueEnglish());
        var mine = await CreateOkAsync(_client, token, UniqueArabic(), UniqueEnglish());
        var id = mine.GetProperty("id").GetInt64();

        var clash = await UpdateAsync(_client, token, id, $"احمد {unique}", UniqueEnglish(), mine.GetProperty("rowVersion").GetString());
        await AssertProblemAsync(clash, HttpStatusCode.Conflict, "error.specialty.name_ar_taken");

        // No change at all.
        var same = await UpdateAsync(_client, token, id, mine.GetProperty("nameAr").GetString()!, mine.GetProperty("nameEn").GetString()!, mine.GetProperty("rowVersion").GetString());
        Assert.Equal(HttpStatusCode.OK, same.StatusCode);

        // Respelling the other record's own name on that record itself.
        var otherId = other.GetProperty("id").GetInt64();
        var respelled = await UpdateAsync(_client, token, otherId, $"إحمد {unique}", other.GetProperty("nameEn").GetString()!, other.GetProperty("rowVersion").GetString());
        Assert.Equal(HttpStatusCode.OK, respelled.StatusCode);
    }

    [Fact]
    public async Task A_deleted_name_can_be_created_again_even_with_a_variant_spelling()
    {
        var token = await ManagerTokenAsync(_fixture, _client);
        var unique = UniqueArabic();
        var nameEn = UniqueEnglish();
        var first = await CreateOkAsync(_client, token, $"مدرسة {unique}", nameEn);
        await _client.SendAsync(Json(HttpMethod.Delete, $"/api/specialties/{first.GetProperty("id").GetInt64()}", token));

        var again = await CreateAsync(_client, token, $"مدرسه {unique}", nameEn);

        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
    }

    [Fact]
    public async Task Two_simultaneous_creates_of_the_same_name_yield_one_201_and_one_409()
    {
        var token = await ManagerTokenAsync(_fixture, _client);
        var nameAr = UniqueArabic();
        var nameEn = UniqueEnglish();

        var results = await Task.WhenAll(
            Enumerable.Range(0, 2).Select(_ => Task.Run(() => CreateAsync(_client, token, nameAr, nameEn))));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        var conflict = Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        var key = (await ProblemAsync(conflict)).GetProperty("title").GetString();
        Assert.Equal("error.specialty.name_ar_taken", key);
        Assert.Single((await ListAsync(_client, token, $"search={Uri.EscapeDataString(nameAr)}")).GetProperty("items").EnumerateArray());
    }
}
