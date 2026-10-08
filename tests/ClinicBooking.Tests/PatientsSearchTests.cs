using ClinicBooking.Tests.Support;
using static ClinicBooking.Tests.Support.PatientHelpers;
using static ClinicBooking.Tests.Support.SpecialtyHelpers;

namespace ClinicBooking.Tests;

/// <summary>One search box: a phone (prefix or contains on the stored E.164 number) or a name (D49, D63).</summary>
[Collection(SqlServerCollection.Name)]
public class PatientsSearchTests : IClassFixture<AuthApiFixture>
{
    private readonly AuthApiFixture _fixture;
    private readonly HttpClient _client;

    public PatientsSearchTests(AuthApiFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Factory.CreateClient();
    }

    private async Task<long[]> SearchAsync(string token, string search) =>
        PatientIds(await ListPatientsAsync(_client, token, $"pageSize=100&search={Uri.EscapeDataString(search)}"));

    [Theory]
    [InlineData("0101234")]             // leading 0: +20 prefix
    [InlineData("010 123-4")]           // spaces and hyphens ignored
    [InlineData("0020101234")]          // leading 00: + prefix
    [InlineData("+20101234")]           // leading +
    [InlineData("٠١٠١٢٣٤")]             // Arabic-Indic digits
    [InlineData("۰۱۰۱۲۳۴")]             // Persian digits
    public async Task A_phone_prefix_finds_the_number_and_not_one_that_only_contains_it(string search)
    {
        var token = await PatientManagerAsync(_fixture, _client);
        var unique = Random.Shared.Next(1000, 9999).ToString();
        var target = await CreatePatientOkAsync(_client, token, phone: $"0101234{unique}");
        // +2011101234.. contains "101234" but does not start with +20101234.
        var other = await CreatePatientOkAsync(_client, token, phone: $"011101234{unique[..2]}");

        var ids = await SearchAsync(token, search);

        Assert.Contains(target.GetProperty("id").GetInt64(), ids);
        Assert.DoesNotContain(other.GetProperty("id").GetInt64(), ids);
    }

    [Fact]
    public async Task Digits_without_a_leading_0_or_plus_match_anywhere_in_the_number()
    {
        var token = await PatientManagerAsync(_fixture, _client);
        var tail = Random.Shared.Next(10_000_000, 99_999_999).ToString();
        var target = await CreatePatientOkAsync(_client, token, phone: "012" + tail);
        var other = await CreatePatientOkAsync(_client, token);

        var ids = await SearchAsync(token, tail[2..]);

        Assert.Contains(target.GetProperty("id").GetInt64(), ids);
        Assert.DoesNotContain(other.GetProperty("id").GetInt64(), ids);
    }

    [Fact]
    public async Task An_international_number_is_found_by_its_plus_or_00_prefix()
    {
        var token = await PatientManagerAsync(_fixture, _client);
        var tail = Random.Shared.Next(1_000_000, 9_999_999).ToString();
        var target = await CreatePatientOkAsync(_client, token, phone: "+4420" + tail);

        Assert.Contains(target.GetProperty("id").GetInt64(), await SearchAsync(token, "+44 20" + tail[..3]));
        Assert.Contains(target.GetProperty("id").GetInt64(), await SearchAsync(token, "004420" + tail[..3]));
    }

    [Fact]
    public async Task Two_digits_are_a_name_search_not_a_phone_search()
    {
        var token = await PatientManagerAsync(_fixture, _client);
        var target = await CreatePatientOkAsync(_client, token, phone: "01012345678");

        Assert.DoesNotContain(target.GetProperty("id").GetInt64(), await SearchAsync(token, "01"));
    }

    public static TheoryData<string, string> ArabicVariants => new()
    {
        { "أحمد", "احمد" },
        { "إسلام", "اسلام" },
        { "فاطمة", "فاطمه" },
        { "مصطفى", "مصطفي" },
        { "مُحَمَّد", "محمد" }
    };

    [Theory]
    [MemberData(nameof(ArabicVariants))]
    public async Task A_name_is_found_whatever_the_Arabic_spelling(string stored, string typed)
    {
        var token = await PatientManagerAsync(_fixture, _client);
        var unique = UniqueArabic();
        var target = await CreatePatientOkAsync(_client, token, $"{stored} {unique}");

        Assert.Equal([target.GetProperty("id").GetInt64()], await SearchAsync(token, $"{typed} {unique}"));
    }

    [Fact]
    public async Task Every_word_of_a_name_search_must_match()
    {
        var token = await PatientManagerAsync(_fixture, _client);
        var unique = UniqueEnglish();
        var both = await CreatePatientOkAsync(_client, token, $"Sara Hany {unique}");
        await CreatePatientOkAsync(_client, token, $"Sara Adel {unique}");

        Assert.Equal([both.GetProperty("id").GetInt64()], await SearchAsync(token, $"hany SARA {unique}"));
    }

    [Fact]
    public async Task Sorting_by_name_and_by_creation_with_paging()
    {
        var token = await PatientManagerAsync(_fixture, _client);
        var unique = UniqueEnglish();
        var created = new List<long>();
        foreach (var name in new[] { "Cc", "Aa", "Bb" })
        {
            created.Add((await CreatePatientOkAsync(_client, token, $"{name} {unique}")).GetProperty("id").GetInt64());
        }

        var first = await ListPatientsAsync(_client, token, $"search={unique}&pageSize=2");
        var second = await ListPatientsAsync(_client, token, $"search={unique}&pageSize=2&page=2");
        var newest = await ListPatientsAsync(_client, token, $"search={unique}&sortBy=createdAt&sortDirection=desc");

        Assert.Equal(3, first.GetProperty("totalCount").GetInt32());
        Assert.Equal([created[1], created[2]], PatientIds(first));
        Assert.Equal([created[0]], PatientIds(second));
        Assert.Equal(Enumerable.Reverse(created).ToArray(), PatientIds(newest));
    }
}
