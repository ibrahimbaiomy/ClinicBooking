using ClinicBooking.Tests.Support;
using static ClinicBooking.Tests.Support.DoctorHelpers;
using static ClinicBooking.Tests.Support.SpecialtyHelpers;

namespace ClinicBooking.Tests;

/// <summary>The doctors list: Arabic-aware search, paging, sorting and the clinic and specialty filters (D49, D50, D61).</summary>
[Collection(SqlServerCollection.Name)]
public class DoctorsSearchTests : IClassFixture<AuthApiFixture>
{
    private readonly AuthApiFixture _fixture;
    private readonly HttpClient _client;

    public DoctorsSearchTests(AuthApiFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Factory.CreateClient();
    }

    private sealed record Setup(long Clinic, long Specialty, string Token);

    private async Task<Setup> SetupAsync()
    {
        var clinic = await NewClinicAsync(_fixture, _client);
        var specialty = await NewSpecialtyAsync(_fixture, _client);
        var (token, _) = await DoctorManagerAsync(_fixture, _client, clinic);
        return new Setup(clinic, specialty, token);
    }

    public static TheoryData<string, string> ArabicVariants => new()
    {
        { "أحمد", "احمد" },
        { "إسلام", "اسلام" },
        { "آمال", "امال" },
        { "فاطمة", "فاطمه" },
        { "مصطفى", "مصطفي" },
        { "مُحَمَّد", "محمد" },
        { "مـحـمـود", "محمود" }
    };

    [Theory]
    [MemberData(nameof(ArabicVariants))]
    public async Task Search_finds_a_name_whatever_the_Arabic_spelling(string stored, string typed)
    {
        var s = await SetupAsync();
        var unique = UniqueArabic();
        var doctor = await CreateDoctorOkAsync(_client, s.Token, [s.Specialty], [s.Clinic], $"د. {stored} {unique}");

        var page = await ListDoctorsAsync(_client, s.Token, $"search={Uri.EscapeDataString($"{typed} {unique}")}");

        Assert.Equal([doctor.GetProperty("id").GetInt64()], Ids(page));
    }

    [Fact]
    public async Task Every_search_word_must_match_one_of_the_names()
    {
        var s = await SetupAsync();
        var unique = UniqueEnglish();
        var both = await CreateDoctorOkAsync(_client, s.Token, [s.Specialty], [s.Clinic], $"سارة {UniqueArabic()}", $"Sara Hany {unique}");
        await CreateDoctorOkAsync(_client, s.Token, [s.Specialty], [s.Clinic], UniqueArabic(), $"Sara Adel {unique}");

        var page = await ListDoctorsAsync(_client, s.Token, $"search={Uri.EscapeDataString($"HANY sara {unique}")}");

        Assert.Equal([both.GetProperty("id").GetInt64()], Ids(page));
    }

    [Fact]
    public async Task Paging_and_sorting_follow_the_shared_list_rules()
    {
        var s = await SetupAsync();
        var unique = UniqueEnglish();
        var created = new List<long>();
        foreach (var name in new[] { "Cc", "Aa", "Bb" })
        {
            created.Add((await CreateDoctorOkAsync(_client, s.Token, [s.Specialty], [s.Clinic], nameEn: $"{name} {unique}")).GetProperty("id").GetInt64());
        }

        var first = await ListDoctorsAsync(_client, s.Token, $"search={unique}&pageSize=2&sortBy=nameEn");
        var second = await ListDoctorsAsync(_client, s.Token, $"search={unique}&pageSize=2&page=2&sortBy=nameEn");
        var newest = await ListDoctorsAsync(_client, s.Token, $"search={unique}&sortBy=createdAt&sortDirection=desc");

        Assert.Equal(3, first.GetProperty("totalCount").GetInt32());
        Assert.Equal([$"Aa {unique}", $"Bb {unique}"], NamesEn(first));
        Assert.Equal([$"Cc {unique}"], NamesEn(second));
        Assert.Equal(Enumerable.Reverse(created).ToArray(), Ids(newest));
    }

    [Fact]
    public async Task The_clinic_and_specialty_filters_narrow_the_list()
    {
        var a = await NewClinicAsync(_fixture, _client);
        var b = await NewClinicAsync(_fixture, _client);
        var cardiology = await NewSpecialtyAsync(_fixture, _client);
        var dermatology = await NewSpecialtyAsync(_fixture, _client);
        var (token, _) = await DoctorManagerAsync(_fixture, _client, a, b);
        var inA = (await CreateDoctorOkAsync(_client, token, [cardiology], [a])).GetProperty("id").GetInt64();
        var inAB = (await CreateDoctorOkAsync(_client, token, [cardiology, dermatology], [a, b])).GetProperty("id").GetInt64();
        var inB = (await CreateDoctorOkAsync(_client, token, [dermatology], [b])).GetProperty("id").GetInt64();

        Assert.Equal(new[] { inA, inAB }.Order(), Ids(await ListDoctorsAsync(_client, token, $"clinicId={a}&pageSize=100")).Order());
        Assert.Equal(new[] { inAB, inB }.Order(), Ids(await ListDoctorsAsync(_client, token, $"specialtyId={dermatology}&pageSize=100")).Order());
        Assert.Equal([inAB], Ids(await ListDoctorsAsync(_client, token, $"clinicId={a}&specialtyId={dermatology}")));
        Assert.Empty(Ids(await ListDoctorsAsync(_client, token, "clinicId=987654321")));
    }
}
