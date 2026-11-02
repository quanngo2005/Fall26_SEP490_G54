using G54.BLL.Common.Import.Parsing;
using G54.DAL.Entities;

namespace G54.UnitTests.Import;

public sealed class CellParsersTests
{
    [Theory]
    [InlineData("hn01", "HN01")]
    [InlineData(" nv001 ", "NV001")]
    public void TryParseCode_ValidInput_NormalizesToUpper(string raw, string expected)
    {
        Assert.True(CellParsers.TryParseCode(raw, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-bad-start")]
    [InlineData("has space")]
    public void TryParseCode_InvalidInput_ReturnsFalse(string? raw)
    {
        Assert.False(CellParsers.TryParseCode(raw, out _));
    }

    [Theory]
    [InlineData("An.Nguyen@Bank.Local", "an.nguyen@bank.local")]
    public void TryParseEmail_ValidInput_LowerCases(string raw, string expected)
    {
        Assert.True(CellParsers.TryParseEmail(raw, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData("long.mt")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParseEmail_InvalidInput_ReturnsFalse(string? raw)
    {
        Assert.False(CellParsers.TryParseEmail(raw, out _));
    }

    [Fact]
    public void TryParseDate_DdMmYyyy_Parses()
    {
        Assert.True(CellParsers.TryParseDate("05/01/2026", out var value));
        Assert.Equal(new DateOnly(2026, 1, 5), value);
    }

    [Theory]
    [InlineData("32/13/2025")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("not-a-date")]
    public void TryParseDate_InvalidInput_ReturnsFalse(string? raw)
    {
        Assert.False(CellParsers.TryParseDate(raw, out _));
    }

    [Fact]
    public void TryParseEnum_MatchesApiVocabulary_CaseInsensitive()
    {
        Assert.True(CellParsers.TryParseEnum<EmployeeStatus>("terminated", out var value));
        Assert.Equal(EmployeeStatus.Terminated, value);
    }

    [Fact]
    public void TryParseEnum_PendingResignation_Parses()
    {
        Assert.True(CellParsers.TryParseEnum<EmployeeStatus>("PENDING_RESIGNATION", out var value));
        Assert.Equal(EmployeeStatus.PendingResignation, value);
    }

    [Fact]
    public void TryParseEnum_UnknownValue_ReturnsFalse()
    {
        Assert.False(CellParsers.TryParseEnum<EmployeeStatus>("CREDIT_OFFICER", out _));
    }
}
