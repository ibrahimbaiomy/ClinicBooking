using System.Net;
using System.Text.Json;
using ClinicBooking.Tests.Support;
using static ClinicBooking.Tests.Support.AuthHelpers;
using static ClinicBooking.Tests.Support.SpecialtyHelpers;

namespace ClinicBooking.Tests;

[Collection(SqlServerCollection.Name)]
public class SpecialtiesSearchTests : IClassFixture<AuthApiFixture>
{
    private readonly AuthApiFixture _fixture;
    private readonly HttpClient _client;

    public SpecialtiesSearchTests(AuthApiFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Factory.CreateClient();
    }

    // Each test uses its own unique tag, so results are independent of the other tests.
    private async Task<JsonElement> SearchAsync(string token, string search, string extra = "") =>
        await ListAsync(_client, token, $"search={Uri.EscapeDataString(search)}{extra}");

    // ---- Arabic letter variants -------------------------------------------------------------

    [Theory]
    [InlineData("أمراض القلب", "امراض القلب")]
    [InlineData("أمراض القلب", "إمراض القلب")]
    [InlineData("أمراض القلب", "أمراض")]
    [InlineData("إدارة", "اداره")]
    [InlineData("آلام الظهر", "الام الظهر")]
    [InlineData("طب الأسنان العامة", "العامه")]
    [InlineData("طب الأسنان العامة", "العامة")]
    [InlineData("مستشفى الأطفال", "مستشفي")]
    [InlineData("مستشفى الأطفال", "مستشفى")]
    [InlineData("قلب", "قَلْب")]
    [InlineData("قَلْب وأوعية", "قلب واوعية")]
    [InlineData("قلـــب", "قلب")]
    [InlineData("عيادة 12", "عيادة ١٢")]
    public async Task Search_ignores_hamza_forms_ta_marbuta_alef_maksura_diacritics_and_digits(string stored, string query)
    {
        var token = await ManagerTokenAsync(_fixture, _client);
        var tag = UniqueArabic();
        await CreateOkAsync(_client, token, $"{stored} {tag}", UniqueEnglish());

        var found = await SearchAsync(token, $"{query} {tag}");

        Assert.Equal(1, found.GetProperty("totalCount").GetInt32());
        Assert.Equal($"{stored} {tag}", NamesAr(found).Single());
    }

    [Fact]
    public async Task Search_does_not_fold_letters_that_D37_does_not_list()
    {
        var token = await ManagerTokenAsync(_fixture, _client);
        var tag = UniqueArabic();
        await CreateOkAsync(_client, token, $"مؤمن {tag}", UniqueEnglish());

        var found = await SearchAsync(token, $"مومن {tag}");

        Assert.Equal(0, found.GetProperty("totalCount").GetInt32());
    }

    // ---- English ----------------------------------------------------------------------------

    [Theory]
    [InlineData("Cardiology", "cardiology")]
    [InlineData("Cardiology", "CARDIO")]
    [InlineData("Cardiology", "  diolog ")]
    [InlineData("Pediatric Surgery", "surgery pediatric")]
    public async Task English_search_is_a_case_insensitive_substring_match(string stored, string query)
    {
        var token = await ManagerTokenAsync(_fixture, _client);
        var tag = UniqueEnglish();
        await CreateOkAsync(_client, token, UniqueArabic(), $"{stored} {tag}");

        var found = await SearchAsync(token, $"{query} {tag}");

        Assert.Equal(1, found.GetProperty("totalCount").GetInt32());
    }

    // ---- both columns, words, edge cases ----------------------------------------------------

    [Fact]
    public async Task One_search_matches_either_name()
    {
        var token = await ManagerTokenAsync(_fixture, _client);
        var tagAr = UniqueArabic();
        var tagEn = UniqueEnglish();
        await CreateOkAsync(_client, token, $"جراحة {tagAr}", $"Surgery {tagEn}");

        Assert.Equal(1, (await SearchAsync(token, tagAr)).GetProperty("totalCount").GetInt32());
        Assert.Equal(1, (await SearchAsync(token, tagEn)).GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Every_word_must_match_and_a_missing_word_excludes_the_row()
    {
        var token = await ManagerTokenAsync(_fixture, _client);
        var tag = UniqueArabic();
        await CreateOkAsync(_client, token, $"أمراض القلب {tag}", UniqueEnglish());

        Assert.Equal(1, (await SearchAsync(token, $"قلب أمراض {tag}")).GetProperty("totalCount").GetInt32());
        Assert.Equal(0, (await SearchAsync(token, $"قلب كلى {tag}")).GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task No_search_returns_everything_including_a_blank_search()
    {
        var token = await ManagerTokenAsync(_fixture, _client);
        var tag = UniqueArabic();
        await CreateOkAsync(_client, token, $"أ {tag}", UniqueEnglish());

        var all = await ListAsync(_client, token, "pageSize=100");
        var blank = await SearchAsync(token, "   ", "&pageSize=100");

        Assert.True(all.GetProperty("totalCount").GetInt32() >= 1);
        Assert.Equal(all.GetProperty("totalCount").GetInt32(), blank.GetProperty("totalCount").GetInt32());
    }

    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("[a-z]")]
    [InlineData("a%b")]
    [InlineData("'; DROP TABLE Specialties; --")]
    public async Task Wildcard_and_sql_characters_are_matched_literally(string query)
    {
        var token = await ManagerTokenAsync(_fixture, _client);
        var tag = UniqueEnglish();
        await CreateOkAsync(_client, token, UniqueArabic(), $"Plain Name {tag}");

        var found = await SearchAsync(token, $"{query} {tag}");

        Assert.Equal(0, found.GetProperty("totalCount").GetInt32());
        // The table is still there.
        Assert.Equal(1, (await SearchAsync(token, tag)).GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Soft_deleted_rows_are_not_found()
    {
        var token = await ManagerTokenAsync(_fixture, _client);
        var tag = UniqueArabic();
        var created = await CreateOkAsync(_client, token, $"حذف {tag}", UniqueEnglish());
        await _client.SendAsync(Json(HttpMethod.Delete, $"/api/specialties/{created.GetProperty("id").GetInt64()}", token));

        var found = await SearchAsync(token, tag);

        Assert.Equal(0, found.GetProperty("totalCount").GetInt32());
    }

    // ---- paging and sorting -----------------------------------------------------------------

    private async Task<(string Token, string Tag, string TagEn)> SeedAsync(int count)
    {
        var token = await ManagerTokenAsync(_fixture, _client);
        var tag = UniqueArabic();
        var tagEn = UniqueEnglish();
        for (var i = 1; i <= count; i++)
        {
            await CreateOkAsync(_client, token, $"{tag} {i}", $"Item {tagEn} {i}");
        }

        return (token, tag, tagEn);
    }

    [Fact]
    public async Task Paging_slices_the_result_and_reports_the_total()
    {
        var (token, tag, _) = await SeedAsync(5);

        var first = await SearchAsync(token, tag, "&pageSize=2&page=1");
        var second = await SearchAsync(token, tag, "&pageSize=2&page=2");
        var third = await SearchAsync(token, tag, "&pageSize=2&page=3");
        var beyond = await SearchAsync(token, tag, "&pageSize=2&page=4");

        Assert.Equal(5, first.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, first.GetProperty("pageSize").GetInt32());
        Assert.Equal(1, first.GetProperty("page").GetInt32());
        Assert.Equal([$"{tag} 1", $"{tag} 2"], NamesAr(first));
        Assert.Equal([$"{tag} 3", $"{tag} 4"], NamesAr(second));
        Assert.Equal([$"{tag} 5"], NamesAr(third));
        Assert.Empty(NamesAr(beyond));
        Assert.Equal(5, beyond.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task The_default_page_size_is_20()
    {
        var token = await ManagerTokenAsync(_fixture, _client);
        var tag = UniqueArabic();
        await Task.WhenAll(Enumerable.Range(1, 21).Select(i => CreateOkAsync(_client, token, $"{tag} {i}", UniqueEnglish())));

        var page = await SearchAsync(token, tag);

        Assert.Equal(21, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(20, page.GetProperty("pageSize").GetInt32());
        Assert.Equal(20, NamesAr(page).Length);
    }

    [Fact]
    public async Task Sorting_by_English_name_works_in_both_directions()
    {
        var (token, tag, tagEn) = await SeedAsync(3);

        var ascending = await SearchAsync(token, tag, "&sortBy=nameEn&sortDirection=asc");
        var descending = await SearchAsync(token, tag, "&sortBy=nameEn&sortDirection=desc");

        Assert.Equal([$"Item {tagEn} 1", $"Item {tagEn} 2", $"Item {tagEn} 3"], NamesEn(ascending));
        Assert.Equal([$"Item {tagEn} 3", $"Item {tagEn} 2", $"Item {tagEn} 1"], NamesEn(descending));
    }

    [Fact]
    public async Task Sorting_by_Arabic_name_follows_the_Arabic_alphabet_ignoring_hamza_forms()
    {
        var token = await ManagerTokenAsync(_fixture, _client);
        var tag = UniqueArabic();
        // Created out of order on purpose. أ sorts as ا (first), then ب, ت, ث, ج.
        foreach (var word in new[] { "جيم", "ثاء", "بكر", "تين", "أمل" })
        {
            await CreateOkAsync(_client, token, $"{word} {tag}", UniqueEnglish());
        }

        var ascending = await SearchAsync(token, tag, "&sortBy=nameAr");
        var descending = await SearchAsync(token, tag, "&sortBy=nameAr&sortDirection=desc");

        Assert.Equal(
            new[] { "أمل", "بكر", "تين", "ثاء", "جيم" }.Select(w => $"{w} {tag}").ToArray(),
            NamesAr(ascending));
        Assert.Equal(
            new[] { "جيم", "ثاء", "تين", "بكر", "أمل" }.Select(w => $"{w} {tag}").ToArray(),
            NamesAr(descending));
    }

    [Fact]
    public async Task Sorting_by_creation_time_puts_the_newest_first_when_descending()
    {
        var (token, tag, _) = await SeedAsync(3);

        var newestFirst = await SearchAsync(token, tag, "&sortBy=createdAt&sortDirection=desc");

        // Same clock tick, so the id tie-break decides: the last created has the highest id.
        Assert.Equal($"{tag} 3", NamesAr(newestFirst).First());
        Assert.Equal(3, newestFirst.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Sort_parameters_are_case_insensitive()
    {
        var (token, tag, _) = await SeedAsync(2);

        var response = await _client.SendAsync(Json(HttpMethod.Get, $"/api/specialties?search={Uri.EscapeDataString(tag)}&sortBy=NAMEEN&sortDirection=DESC", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
