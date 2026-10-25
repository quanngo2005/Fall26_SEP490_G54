using System.Globalization;
using G54.BLL.Services;
using Xunit;

namespace G54.UnitTests;

public sealed class ReferralPointSplitTests
{
    [Fact]
    public void Split_WithThirtyPercent_GivesReferrerThirtyAndProcessorSeventy()
    {
        var (referrer, processor) = ReferralPointSplit.Split(50m, 30m);

        Assert.Equal(15m, referrer);
        Assert.Equal(35m, processor);
    }

    [Theory]
    [InlineData("100", "50", "50")]
    [InlineData("100", "25", "75")]
    [InlineData("100", "0", "100")]
    [InlineData("100", "100", "0")]
    [InlineData("80", "12.5", "70")]
    public void Split_UsesTheGivenReferrerShare(string total, string sharePercent, string expectedProcessor)
    {
        var (referrer, processor) = ReferralPointSplit.Split(Parse(total), Parse(sharePercent));

        Assert.Equal(Parse(total) - Parse(expectedProcessor), referrer);
        Assert.Equal(Parse(expectedProcessor), processor);
    }

    [Theory]
    [InlineData("0", "30")]
    [InlineData("1", "30")]
    [InlineData("33.3333", "30")]
    [InlineData("0.0001", "30")]
    [InlineData("12345.6789", "30")]
    [InlineData("12345.6789", "33.3333")]
    [InlineData("7", "66.6667")]
    public void Split_PartsAlwaysAddUpToTheTotal(string total, string sharePercent)
    {
        var points = Parse(total);

        var (referrer, processor) = ReferralPointSplit.Split(points, Parse(sharePercent));

        Assert.Equal(points, referrer + processor);
    }

    [Fact]
    public void Split_RoundsReferrerPartToFourDecimalPlaces()
    {
        var (referrer, processor) = ReferralPointSplit.Split(0.0001m, 30m);

        Assert.Equal(0m, referrer);
        Assert.Equal(0.0001m, processor);
    }

    [Fact]
    public void Split_RoundsHalfAwayFromZero()
    {
        // 0.0005 * 30% = 0.00015 -> 0.0002 when rounded half away from zero to 4 places.
        var (referrer, processor) = ReferralPointSplit.Split(0.0005m, 30m);

        Assert.Equal(0.0002m, referrer);
        Assert.Equal(0.0003m, processor);
    }

    [Fact]
    public void Split_ZeroPoints_GivesZeroToBoth()
    {
        var (referrer, processor) = ReferralPointSplit.Split(0m, 30m);

        Assert.Equal(0m, referrer);
        Assert.Equal(0m, processor);
    }

    [Fact]
    public void Split_NegativePoints_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ReferralPointSplit.Split(-1m, 30m));
    }

    [Theory]
    [InlineData("-0.0001")]
    [InlineData("100.0001")]
    [InlineData("-30")]
    [InlineData("150")]
    public void Split_ReferrerShareOutsideZeroToHundred_Throws(string sharePercent)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ReferralPointSplit.Split(50m, Parse(sharePercent)));
    }

    private static decimal Parse(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
}
