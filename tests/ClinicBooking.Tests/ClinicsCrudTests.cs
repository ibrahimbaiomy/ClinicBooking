using System.Net;
using System.Text.Json;
using ClinicBooking.Application.DTOs;
using ClinicBooking.Application.Interfaces;
using ClinicBooking.Domain.Exceptions;
using ClinicBooking.Domain.Permissions;
using ClinicBooking.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using static ClinicBooking.Tests.Support.AuthHelpers;
using static ClinicBooking.Tests.Support.ClinicHelpers;
using static ClinicBooking.Tests.Support.SpecialtyHelpers;

namespace ClinicBooking.Tests;

[Collection(SqlServerCollection.Name)]
public class ClinicsCrudTests : IClassFixture<AuthApiFixture>
{
    private static readonly DateTimeOffset BaseTime = new(2026, 7, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly AuthApiFixture _fixture;
    private readonly HttpClient _client;

    public ClinicsCrudTests(AuthApiFixture fixture)
    {
        _fixture = fixture;
        _fixture.Clock.Now = BaseTime;
        _client = fixture.Factory.CreateClient();
    }

    // ---- validation -------------------------------------------------------------------------

    public static TheoryData<string?, string?, string, string> InvalidNames => new()
    {
        { "", "Valid", "nameAr", "error.clinic.name_ar_required" },
        { "   ", "Valid", "nameAr", "error.clinic.name_ar_required" },
        { null, "Valid", "nameAr", "error.clinic.name_ar_required" },
        { "صحيح", "", "nameEn", "error.clinic.name_en_required" },
        { "صحيح", null, "nameEn", "error.clinic.name_en_required" },
        { new string('ب', 101), "Valid", "nameAr", "error.clinic.name_ar_too_long" },
        { "صحيح", new string('a', 101), "nameEn", "error.clinic.name_en_too_long" },
        { "ـً", "Valid", "nameAr", "error.clinic.name_ar_invalid" }
    };

    [Theory]
    [MemberData(nameof(InvalidNames))]
    public async Task Create_rejects_invalid_names_with_keys(string? nameAr, string? nameEn, string field, string key)
    {
        var token = await ClinicManagerTokenAsync(_fixture, _client);

        var response = await _client.SendAsync(Json(HttpMethod.Post, "/api/clinics", token, new { nameAr, nameEn }));

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "error.validation.failed");
        Assert.Equal(key, (await ProblemAsync(response)).GetProperty("errors").GetProperty(field)[0].GetString());
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("12345")]
    [InlineData("+0123456789")]
    [InlineData("01312345678")]
    [InlineData("16123")]
    [InlineData("٠١٠١٢٣٤٥٦٧٨x")]
    public async Task Create_rejects_an_invalid_phone_with_a_key(string phone)
    {
        var token = await ClinicManagerTokenAsync(_fixture, _client);

        var response = await CreateClinicAsync(_client, token, UniqueArabic(), UniqueEnglish(), phone: phone);

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "error.validation.failed");
        Assert.Equal("error.clinic.phone_invalid", (await ProblemAsync(response)).GetProperty("errors").GetProperty("phone")[0].GetString());
    }

    [Fact]
    public async Task Create_rejects_a_phone_longer_than_32_characters_without_parsing_it()
    {
        var token = await ClinicManagerTokenAsync(_fixture, _client);

        var response = await CreateClinicAsync(_client, token, UniqueArabic(), UniqueEnglish(), phone: "01012345678" + new string(' ', 30));

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "error.validation.failed");
        Assert.Equal("error.clinic.phone_invalid", (await ProblemAsync(response)).GetProperty("errors").GetProperty("phone")[0].GetString());
    }

    [Fact]
    public async Task Create_rejects_an_address_over_300_characters_and_accepts_exactly_300()
    {
        var token = await ClinicManagerTokenAsync(_fixture, _client);

        var tooLong = await CreateClinicAsync(_client, token, UniqueArabic(), UniqueEnglish(), address: new string('ش', 301));
        var exact = await CreateClinicAsync(_client, token, UniqueArabic(), UniqueEnglish(), address: new string('ش', 300));

        await AssertProblemAsync(tooLong, HttpStatusCode.BadRequest, "error.validation.failed");
        Assert.Equal("error.clinic.address_too_long", (await ProblemAsync(tooLong)).GetProperty("errors").GetProperty("address")[0].GetString());
        Assert.Equal(HttpStatusCode.Created, exact.StatusCode);
    }

    [Fact]
    public async Task Create_reports_every_failing_field_at_once()
    {
        var token = await ClinicManagerTokenAsync(_fixture, _client);

        var response = await _client.SendAsync(Json(HttpMethod.Post, "/api/clinics", token, new { address = new string('a', 301), phone = "nope" }));

        var errors = (await ProblemAsync(response)).GetProperty("errors");
        Assert.Equal("error.clinic.name_ar_required", errors.GetProperty("nameAr")[0].GetString());
        Assert.Equal("error.clinic.name_en_required", errors.GetProperty("nameEn")[0].GetString());
        Assert.Equal("error.clinic.address_too_long", errors.GetProperty("address")[0].GetString());
        Assert.Equal("error.clinic.phone_invalid", errors.GetProperty("phone")[0].GetString());
    }

    [Fact]
    public async Task A_malformed_body_is_a_400_with_keys()
    {
        var token = await ClinicManagerTokenAsync(_fixture, _client);
        var request = WithBearer(HttpMethod.Post, "/api/clinics", token);
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
        var token = await ClinicManagerTokenAsync(_fixture, _client);
        var created = await CreateClinicOkAsync(_client, token, UniqueArabic(), UniqueEnglish());

        var response = await UpdateClinicAsync(_client, token, created.GetProperty("id").GetInt64(), UniqueArabic(), UniqueEnglish(), null, null, rowVersion);

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
        var token = await ClinicManagerTokenAsync(_fixture, _client);

        var response = await _client.SendAsync(Json(HttpMethod.Get, $"/api/clinics?{query}", token));

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "error.validation.failed");
        Assert.Equal(key, (await ProblemAsync(response)).GetProperty("errors").GetProperty(field)[0].GetString());
    }

    [Fact]
    public async Task List_rejects_a_search_that_is_too_long_and_a_non_numeric_page()
    {
        var token = await ClinicManagerTokenAsync(_fixture, _client);

        var longSearch = await _client.SendAsync(Json(HttpMethod.Get, $"/api/clinics?search={new string('a', 101)}", token));
        var badPage = await _client.SendAsync(Json(HttpMethod.Get, "/api/clinics?page=abc", token));

        await AssertProblemAsync(longSearch, HttpStatusCode.BadRequest, "error.validation.failed");
        Assert.Equal("error.search.too_long", (await ProblemAsync(longSearch)).GetProperty("errors").GetProperty("search")[0].GetString());
        await AssertProblemAsync(badPage, HttpStatusCode.BadRequest, "error.validation.failed");
    }

    // A value that slips past the validator (a caller other than the controller) is a 400 key, never a 500.
    [Fact]
    public async Task A_phone_that_bypasses_the_validator_is_an_InvalidRequestException_and_creates_nothing()
    {
        _ = _fixture.Factory.CreateClient();
        var nameEn = UniqueEnglish();
        using var scope = _fixture.Factory.Services.CreateScope();
        var clinics = scope.ServiceProvider.GetRequiredService<IClinicService>();

        var failure = await Assert.ThrowsAsync<InvalidRequestException>(
            () => clinics.CreateAsync(new CreateClinicRequest(UniqueArabic(), nameEn, null, "not a phone"), CancellationToken.None));

        Assert.Equal("error.clinic.phone_invalid", failure.ErrorKey);
        var token = await ClinicManagerTokenAsync(_fixture, _client);
        Assert.Equal(0, (await ListClinicsAsync(_client, token, $"search={Uri.EscapeDataString(nameEn)}")).GetProperty("totalCount").GetInt32());
    }

    // ---- create, read, update, delete -------------------------------------------------------

    [Fact]
    public async Task Create_returns_201_with_a_location_trimmed_names_and_a_normalised_phone()
    {
        var token = await ClinicManagerTokenAsync(_fixture, _client);
        var nameAr = UniqueArabic();
        var nameEn = UniqueEnglish();

        var response = await CreateClinicAsync(_client, token, $"  {nameAr}  ", $" {nameEn} ", "  12 شارع النيل، الجيزة  ", "٠١٠ ١٢٣٤ ٥٦٧٨");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ProblemAsync(response);
        var id = body.GetProperty("id").GetInt64();
        Assert.Equal($"/api/clinics/{id}", response.Headers.Location!.AbsolutePath);
        Assert.Equal(nameAr, body.GetProperty("nameAr").GetString());
        Assert.Equal(nameEn, body.GetProperty("nameEn").GetString());
        Assert.Equal("12 شارع النيل، الجيزة", body.GetProperty("address").GetString());
        Assert.Equal("+201012345678", body.GetProperty("phone").GetString());
        Assert.Equal(BaseTime, body.GetProperty("createdAt").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("updatedAt").ValueKind);
        Assert.Equal(12, body.GetProperty("rowVersion").GetString()!.Length);
        Assert.False(body.TryGetProperty("nameArNormalized", out _));
        Assert.False(body.TryGetProperty("isDeleted", out _));
    }

    [Fact]
    public async Task Address_and_phone_are_optional_and_blank_values_are_stored_as_absent()
    {
        var token = await ClinicManagerTokenAsync(_fixture, _client);

        var omitted = await CreateClinicOkAsync(_client, token, UniqueArabic(), UniqueEnglish());
        var blank = await CreateClinicOkAsync(_client, token, UniqueArabic(), UniqueEnglish(), "   ", "  ");

        foreach (var clinic in new[] { omitted, blank })
        {
            Assert.Equal(JsonValueKind.Null, clinic.GetProperty("address").ValueKind);
            Assert.Equal(JsonValueKind.Null, clinic.GetProperty("phone").ValueKind);
        }
    }

    [Fact]
    public async Task Get_returns_the_clinic_and_404_for_an_unknown_id()
    {
        var token = await ClinicManagerTokenAsync(_fixture, _client);
        var created = await CreateClinicOkAsync(_client, token, UniqueArabic(), UniqueEnglish(), "Address", "01012345678");

        var found = await _client.SendAsync(Json(HttpMethod.Get, $"/api/clinics/{created.GetProperty("id").GetInt64()}", token));
        var missing = await _client.SendAsync(Json(HttpMethod.Get, "/api/clinics/987654321", token));

        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        var body = await ProblemAsync(found);
        Assert.Equal("Address", body.GetProperty("address").GetString());
        Assert.Equal("+201012345678", body.GetProperty("phone").GetString());
        await AssertProblemAsync(missing, HttpStatusCode.NotFound, "error.clinic.not_found");
    }

    [Fact]
    public async Task Update_changes_everything_and_the_row_version()
    {
        var token = await ClinicManagerTokenAsync(_fixture, _client);
        var created = await CreateClinicOkAsync(_client, token, UniqueArabic(), UniqueEnglish(), "Old address", "01012345678");
        var id = created.GetProperty("id").GetInt64();
        var newAr = UniqueArabic();
        var newEn = UniqueEnglish();

        var later = BaseTime.AddMinutes(5);
        _fixture.Clock.Now = later;
        var response = await UpdateClinicAsync(_client, token, id, newAr, newEn, "New address", "+20 2 2345 6789", created.GetProperty("rowVersion").GetString());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await ProblemAsync(response);
        Assert.Equal(newAr, updated.GetProperty("nameAr").GetString());
        Assert.Equal(newEn, updated.GetProperty("nameEn").GetString());
        Assert.Equal("New address", updated.GetProperty("address").GetString());
        Assert.Equal("+20223456789", updated.GetProperty("phone").GetString());
        Assert.Equal(BaseTime, updated.GetProperty("createdAt").GetDateTimeOffset());
        Assert.Equal(later, updated.GetProperty("updatedAt").GetDateTimeOffset());
        Assert.NotEqual(created.GetProperty("rowVersion").GetString(), updated.GetProperty("rowVersion").GetString());
    }

    [Fact]
    public async Task Update_is_a_full_replace_so_an_omitted_or_blank_address_and_phone_are_cleared()
    {
        var token = await ClinicManagerTokenAsync(_fixture, _client);
        var created = await CreateClinicOkAsync(_client, token, UniqueArabic(), UniqueEnglish(), "Address", "01012345678");
        var id = created.GetProperty("id").GetInt64();

        var omitted = await _client.SendAsync(Json(HttpMethod.Put, $"/api/clinics/{id}", token, new
        {
            nameAr = created.GetProperty("nameAr").GetString(),
            nameEn = created.GetProperty("nameEn").GetString(),
            rowVersion = created.GetProperty("rowVersion").GetString()
        }));

        Assert.Equal(HttpStatusCode.OK, omitted.StatusCode);
        var cleared = await ProblemAsync(omitted);
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("address").ValueKind);
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("phone").ValueKind);

        var filled = await UpdateClinicAsync(_client, token, id, cleared.GetProperty("nameAr").GetString()!, cleared.GetProperty("nameEn").GetString()!, "Back", "01112345678", cleared.GetProperty("rowVersion").GetString());
        var blanked = await UpdateClinicAsync(_client, token, id, cleared.GetProperty("nameAr").GetString()!, cleared.GetProperty("nameEn").GetString()!, "  ", " ", (await ProblemAsync(filled)).GetProperty("rowVersion").GetString());
        Assert.Equal(JsonValueKind.Null, (await ProblemAsync(blanked)).GetProperty("phone").ValueKind);
    }

    [Fact]
    public async Task Update_rejects_an_invalid_phone_and_changes_nothing()
    {
        var token = await ClinicManagerTokenAsync(_fixture, _client);
        var created = await CreateClinicOkAsync(_client, token, UniqueArabic(), UniqueEnglish(), "Keep", "01012345678");
        var id = created.GetProperty("id").GetInt64();

        var response = await UpdateClinicAsync(_client, token, id, UniqueArabic(), UniqueEnglish(), "Changed", "bad", created.GetProperty("rowVersion").GetString());

        await AssertProblemAsync(response, HttpStatusCode.BadRequest, "error.validation.failed");
        var current = await ProblemAsync(await _client.SendAsync(Json(HttpMethod.Get, $"/api/clinics/{id}", token)));
        Assert.Equal("Keep", current.GetProperty("address").GetString());
        Assert.Equal(created.GetProperty("nameAr").GetString(), current.GetProperty("nameAr").GetString());
    }

    [Fact]
    public async Task Update_of_an_unknown_id_is_404()
    {
        var token = await ClinicManagerTokenAsync(_fixture, _client);

        var response = await UpdateClinicAsync(_client, token, 987654321, UniqueArabic(), UniqueEnglish(), null, null, "AAAAAAAAAAA=");

        await AssertProblemAsync(response, HttpStatusCode.NotFound, "error.clinic.not_found");
    }

    [Fact]
    public async Task A_stale_row_version_is_a_409_and_the_current_one_succeeds()
    {
        var token = await ClinicManagerTokenAsync(_fixture, _client);
        var created = await CreateClinicOkAsync(_client, token, UniqueArabic(), UniqueEnglish());
        var id = created.GetProperty("id").GetInt64();
        var original = created.GetProperty("rowVersion").GetString();

        var first = await UpdateClinicAsync(_client, token, id, UniqueArabic(), UniqueEnglish(), "A", null, original);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var stale = await UpdateClinicAsync(_client, token, id, UniqueArabic(), UniqueEnglish(), "B", null, original);
        await AssertProblemAsync(stale, HttpStatusCode.Conflict, "error.concurrency.conflict");

        var current = (await ProblemAsync(first)).GetProperty("rowVersion").GetString();
        var retry = await UpdateClinicAsync(_client, token, id, UniqueArabic(), UniqueEnglish(), "C", null, current);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
    }

    [Fact]
    public async Task Delete_is_a_soft_delete_that_hides_the_row_everywhere()
    {
        var (token, userId) = await SignInAsync(_fixture, _client, Permissions.Clinics.Manage);
        var nameAr = UniqueArabic();
        var created = await CreateClinicOkAsync(_client, token, nameAr, UniqueEnglish());
        var id = created.GetProperty("id").GetInt64();

        var deleted = await _client.SendAsync(Json(HttpMethod.Delete, $"/api/clinics/{id}", token));

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var get = await _client.SendAsync(Json(HttpMethod.Get, $"/api/clinics/{id}", token));
        await AssertProblemAsync(get, HttpStatusCode.NotFound, "error.clinic.not_found");
        Assert.Empty((await ListClinicsAsync(_client, token, $"search={Uri.EscapeDataString(nameAr)}")).GetProperty("items").EnumerateArray());
        var again = await _client.SendAsync(Json(HttpMethod.Delete, $"/api/clinics/{id}", token));
        await AssertProblemAsync(again, HttpStatusCode.NotFound, "error.clinic.not_found");
        var update = await UpdateClinicAsync(_client, token, id, UniqueArabic(), UniqueEnglish(), null, null, created.GetProperty("rowVersion").GetString());
        await AssertProblemAsync(update, HttpStatusCode.NotFound, "error.clinic.not_found");

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var row = await db.Clinics.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == id);
        Assert.True(row.IsDeleted);
        Assert.Equal(userId, row.DeletedBy);
    }

    [Fact]
    public async Task Audit_fields_record_the_real_user_of_each_step()
    {
        var (creator, creatorId) = await SignInAsync(_fixture, _client, Permissions.Clinics.Manage);
        var (editor, editorId) = await SignInAsync(_fixture, _client, Permissions.Clinics.Manage);
        var (remover, removerId) = await SignInAsync(_fixture, _client, Permissions.Clinics.Manage);

        var created = await CreateClinicOkAsync(_client, creator, UniqueArabic(), UniqueEnglish());
        var id = created.GetProperty("id").GetInt64();
        _fixture.Clock.Now = BaseTime.AddMinutes(1);
        var edited = await UpdateClinicAsync(_client, editor, id, UniqueArabic(), UniqueEnglish(), "x", null, created.GetProperty("rowVersion").GetString());
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var afterEdit = await db.Clinics.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == id);
            Assert.Equal(creatorId, afterEdit.CreatedBy);
            Assert.Equal(editorId, afterEdit.UpdatedBy);
            Assert.Equal(BaseTime.AddMinutes(1), afterEdit.UpdatedAt);
        }

        _fixture.Clock.Now = BaseTime.AddMinutes(2);
        await _client.SendAsync(Json(HttpMethod.Delete, $"/api/clinics/{id}", remover));

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var row = await db.Clinics.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == id);
            Assert.Equal(creatorId, row.CreatedBy);
            Assert.Equal(BaseTime, row.CreatedAt);
            Assert.Equal(removerId, row.UpdatedBy);
            Assert.Equal(removerId, row.DeletedBy);
            Assert.Equal(BaseTime.AddMinutes(2), row.DeletedAt);
        }
    }

    // ---- duplicates -------------------------------------------------------------------------

    [Fact]
    public async Task Exact_duplicates_are_409_with_the_matching_key()
    {
        var token = await ClinicManagerTokenAsync(_fixture, _client);
        var nameAr = UniqueArabic();
        var nameEn = UniqueEnglish();
        await CreateClinicOkAsync(_client, token, nameAr, nameEn);

        var sameAr = await CreateClinicAsync(_client, token, nameAr, UniqueEnglish());
        var sameEn = await CreateClinicAsync(_client, token, UniqueArabic(), nameEn);

        await AssertProblemAsync(sameAr, HttpStatusCode.Conflict, "error.clinic.name_ar_taken");
        await AssertProblemAsync(sameEn, HttpStatusCode.Conflict, "error.clinic.name_en_taken");
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
        var token = await ClinicManagerTokenAsync(_fixture, _client);
        var unique = UniqueArabic();
        await CreateClinicOkAsync(_client, token, $"{original} {unique}", UniqueEnglish());

        var duplicate = await CreateClinicAsync(_client, token, $"{variant} {unique}", UniqueEnglish());

        await AssertProblemAsync(duplicate, HttpStatusCode.Conflict, "error.clinic.name_ar_taken");
    }

    [Fact]
    public async Task English_names_differing_only_in_case_or_spacing_are_duplicates()
    {
        var token = await ClinicManagerTokenAsync(_fixture, _client);
        var unique = UniqueEnglish();
        await CreateClinicOkAsync(_client, token, UniqueArabic(), $"Nile Dental Clinic {unique}");

        var duplicate = await CreateClinicAsync(_client, token, UniqueArabic(), $"  nile   DENTAL clinic {unique} ");

        await AssertProblemAsync(duplicate, HttpStatusCode.Conflict, "error.clinic.name_en_taken");
    }

    [Fact]
    public async Task A_clinic_and_a_specialty_may_share_a_name()
    {
        var token = await ClinicManagerTokenAsync(_fixture, _client);
        var manager = await ManagerTokenAsync(_fixture, _client);
        var nameAr = UniqueArabic();
        var nameEn = UniqueEnglish();
        await CreateOkAsync(_client, manager, nameAr, nameEn);

        var clinic = await CreateClinicAsync(_client, token, nameAr, nameEn);

        Assert.Equal(HttpStatusCode.Created, clinic.StatusCode);
    }

    [Fact]
    public async Task Editing_into_another_names_is_409_but_keeping_or_respelling_your_own_is_fine()
    {
        var token = await ClinicManagerTokenAsync(_fixture, _client);
        var unique = UniqueArabic();
        var other = await CreateClinicOkAsync(_client, token, $"أحمد {unique}", UniqueEnglish());
        var mine = await CreateClinicOkAsync(_client, token, UniqueArabic(), UniqueEnglish());
        var id = mine.GetProperty("id").GetInt64();

        var clash = await UpdateClinicAsync(_client, token, id, $"احمد {unique}", UniqueEnglish(), null, null, mine.GetProperty("rowVersion").GetString());
        await AssertProblemAsync(clash, HttpStatusCode.Conflict, "error.clinic.name_ar_taken");

        var same = await UpdateClinicAsync(_client, token, id, mine.GetProperty("nameAr").GetString()!, mine.GetProperty("nameEn").GetString()!, null, null, mine.GetProperty("rowVersion").GetString());
        Assert.Equal(HttpStatusCode.OK, same.StatusCode);

        var otherId = other.GetProperty("id").GetInt64();
        var respelled = await UpdateClinicAsync(_client, token, otherId, $"إحمد {unique}", other.GetProperty("nameEn").GetString()!, null, null, other.GetProperty("rowVersion").GetString());
        Assert.Equal(HttpStatusCode.OK, respelled.StatusCode);
    }

    [Fact]
    public async Task A_deleted_name_can_be_created_again_even_with_a_variant_spelling()
    {
        var token = await ClinicManagerTokenAsync(_fixture, _client);
        var unique = UniqueArabic();
        var nameEn = UniqueEnglish();
        var first = await CreateClinicOkAsync(_client, token, $"مدرسة {unique}", nameEn);
        await _client.SendAsync(Json(HttpMethod.Delete, $"/api/clinics/{first.GetProperty("id").GetInt64()}", token));

        var again = await CreateClinicAsync(_client, token, $"مدرسه {unique}", nameEn);

        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
    }

    [Fact]
    public async Task Two_simultaneous_creates_of_the_same_name_yield_one_201_and_one_409()
    {
        var token = await ClinicManagerTokenAsync(_fixture, _client);
        var nameAr = UniqueArabic();
        var nameEn = UniqueEnglish();

        var results = await Task.WhenAll(
            Enumerable.Range(0, 2).Select(_ => Task.Run(() => CreateClinicAsync(_client, token, nameAr, nameEn))));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.Created));
        var conflict = Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
        var key = (await ProblemAsync(conflict)).GetProperty("title").GetString();
        Assert.Equal("error.clinic.name_ar_taken", key);
        Assert.Single((await ListClinicsAsync(_client, token, $"search={Uri.EscapeDataString(nameAr)}")).GetProperty("items").EnumerateArray());
    }
}
