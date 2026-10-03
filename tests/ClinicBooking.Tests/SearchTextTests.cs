using ClinicBooking.Domain.ValueObjects;

namespace ClinicBooking.Tests;

/// <summary>Pure logic with real branching, so unit tests are right here (D30).</summary>
public class SearchTextTests
{
    [Theory]
    // alef variants fold to bare alef
    [InlineData("أحمد", "احمد")]
    [InlineData("إحمد", "احمد")]
    [InlineData("آحمد", "احمد")]
    [InlineData("ٱحمد", "احمد")]
    // ta marbuta -> ha, alef maksura -> ya
    [InlineData("مدرسة", "مدرسه")]
    [InlineData("مستشفى", "مستشفي")]
    // diacritics and tatweel are dropped
    [InlineData("قَلْب", "قلب")]
    [InlineData("مُحَمَّد", "محمد")]
    [InlineData("قلـــب", "قلب")]
    [InlineData("الله", "الله")]
    [InlineData("اللّٰه", "الله")]
    // digits
    [InlineData("عيادة ١٢٣", "عياده 123")]
    [InlineData("عيادة ۴۵۶", "عياده 456")]
    // presentation forms and ligatures (NFKC)
    [InlineData("ﻻ", "لا")]
    [InlineData("ﺃﺣﻤﺪ", "احمد")]
    // invisible format characters
    [InlineData("قل‏ب", "قلب")]
    [InlineData("قل‌ب", "قلب")]
    // English: case and whitespace
    [InlineData("Cardiology", "cardiology")]
    [InlineData("  Pediatric   Surgery ", "pediatric surgery")]
    [InlineData("Ear\tNose\nThroat", "ear nose throat")]
    // mixed
    [InlineData("طب الأسنان (Dental)", "طب الاسنان (dental)")]
    public void Normalize_folds_what_D37_lists_and_nothing_more(string input, string expected)
    {
        Assert.Equal(expected, SearchText.Normalize(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ـً")]
    public void Normalize_returns_empty_for_nothing_searchable(string? input)
    {
        Assert.Equal(string.Empty, SearchText.Normalize(input));
    }

    [Theory]
    [InlineData("أَحْمَد")]
    [InlineData("  Cardiology  ")]
    [InlineData("مستشفى ١٢")]
    public void Normalize_is_idempotent(string input)
    {
        var once = SearchText.Normalize(input);

        Assert.Equal(once, SearchText.Normalize(once));
    }

    [Fact]
    public void Letters_that_D37_does_not_list_are_kept_distinct()
    {
        Assert.NotEqual(SearchText.Normalize("مؤمن"), SearchText.Normalize("مومن"));
        Assert.NotEqual(SearchText.Normalize("رئيس"), SearchText.Normalize("رييس"));
    }
}
