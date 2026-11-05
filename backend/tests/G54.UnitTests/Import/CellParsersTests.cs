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
}
