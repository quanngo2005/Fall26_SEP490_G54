using G54.BLL.Common.Import.Parsing;
using G54.DAL.Entities;

namespace G54.UnitTests.Import;

public sealed class CellParsersTests
{
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

    [Theory]
    [InlineData("05/10/2026", 2026, 10, 5)]
    [InlineData("5/10/2026", 2026, 10, 5)]
    [InlineData("20/10/2026", 2026, 10, 20)]
    [InlineData("2026-10-05", 2026, 10, 5)]
    public void TryParseDate_AcceptsVietnameseAndIsoFormatsOnly(string raw, int year, int month, int day)
    {
        Assert.True(CellParsers.TryParseDate(raw, out var date));
        Assert.Equal(new DateOnly(year, month, day), date);
    }

    [Theory]
    [InlineData("")]
    [InlineData("10/20/2026")]
    [InlineData("32/01/2026")]
    [InlineData("not-a-date")]
    public void TryParseDate_RejectsUsAndInvalidDates(string raw)
    {
        Assert.False(CellParsers.TryParseDate(raw, out _));
    }

    [Theory]
    [InlineData("15.000.000", 15000000)]
    [InlineData("15,000,000", 15000000)]
    [InlineData("15 000 000", 15000000)]
    [InlineData("-500000", -500000)]
    [InlineData("1234.56", 1234.56)]
    [InlineData("0", 0)]
    public void TryParseAmount_AcceptsThousandsSeparatorsAndDecimals(string raw, decimal expected)
    {
        Assert.True(CellParsers.TryParseAmount(raw, out var amount));
        Assert.Equal(expected, amount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("15.00.000")]
    public void TryParseAmount_RejectsInvalidText(string raw)
    {
        Assert.False(CellParsers.TryParseAmount(raw, out _));
    }
}
