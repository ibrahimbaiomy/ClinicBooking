using ClinicBooking.Domain.ValueObjects;

namespace ClinicBooking.Tests;

/// <summary>How one search box is read as a phone or a name (D63; D30: unit tests for real branching).</summary>
public class PatientSearchTermTests
{
    [Theory]
    [InlineData("010", PatientSearchKind.PhonePrefix, "+2010")]
    [InlineData("0101234", PatientSearchKind.PhonePrefix, "+20101234")]
    [InlineData("010 1234-5678", PatientSearchKind.PhonePrefix, "+201012345678")]
    [InlineData("00201012", PatientSearchKind.PhonePrefix, "+201012")]
    [InlineData("+2010", PatientSearchKind.PhonePrefix, "+2010")]
    [InlineData("+44 20", PatientSearchKind.PhonePrefix, "+4420")]
    [InlineData("1012345", PatientSearchKind.PhoneContains, "1012345")]
    [InlineData("345", PatientSearchKind.PhoneContains, "345")]
    [InlineData("٠١٠١٢", PatientSearchKind.PhonePrefix, "+201012")]
    [InlineData("۰۱۰۱۲", PatientSearchKind.PhonePrefix, "+201012")]
    [InlineData("١٢٣٤", PatientSearchKind.PhoneContains, "1234")]
    [InlineData("  0 1 0  ", PatientSearchKind.PhonePrefix, "+2010")]
    public void Digits_are_a_phone_search(string query, PatientSearchKind kind, string phone)
    {
        var term = PatientSearchTerm.Parse(query);

        Assert.Equal(kind, term.Kind);
        Assert.Equal(phone, term.Phone);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12")] // fewer than 3 digits
    [InlineData("+1")]
    [InlineData("أحمد")]
    [InlineData("Ahmed 010")] // letters and digits: a name
    [InlineData("010.123")] // a dot is not ignored
    [InlineData("010+1")] // a + only at the start
    [InlineData("(010)")]
    public void Anything_else_is_a_name_search(string? query)
    {
        Assert.Equal(PatientSearchKind.Name, PatientSearchTerm.Parse(query).Kind);
    }
}
