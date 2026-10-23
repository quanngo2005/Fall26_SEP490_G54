using G54.BLL.Services;
using Xunit;

namespace G54.UnitTests;

public sealed class ReferralPointSplitTests
{
    [Fact]
    public void Split_GivesReferrerThirtyPercentAndProcessorSeventyPercent()
    {
        var (referrer, processor) = ReferralPointSplit.Split(50m);

        Assert.Equal(15m, referrer);
        Assert.Equal(35m, processor);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("33.3333")]
    [InlineData("0.0001")]
    [InlineData("12345.6789")]
    public void Split_PartsAlwaysAddUpToTheTotal(string total)
    {
        var points = decimal.Parse(total, System.Globalization.CultureInfo.InvariantCulture);

        var (referrer, processor) = ReferralPointSplit.Split(points);

        Assert.Equal(points, referrer + processor);
    }

    [Fact]
    public void Split_RoundsReferrerPartToFourDecimalPlaces()
    {
        var (referrer, processor) = ReferralPointSplit.Split(0.0001m);

        Assert.Equal(0m, referrer);
        Assert.Equal(0.0001m, processor);
    }

    [Fact]
    public void Split_RoundsHalfAwayFromZero()
    {
        // 0.0005 * 0.30 = 0.00015 -> 0.0002 when rounded half away from zero to 4 places.
        var (referrer, processor) = ReferralPointSplit.Split(0.0005m);

        Assert.Equal(0.0002m, referrer);
        Assert.Equal(0.0003m, processor);
    }

    [Fact]
    public void Split_ZeroPoints_GivesZeroToBoth()
    {
        var (referrer, processor) = ReferralPointSplit.Split(0m);

        Assert.Equal(0m, referrer);
        Assert.Equal(0m, processor);
    }

    [Fact]
    public void Split_NegativePoints_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ReferralPointSplit.Split(-1m));
    }

    [Fact]
    public void Shares_AddUpToOneHundredPercent()
    {
        Assert.Equal(1m, ReferralPointSplit.ReferrerShare + ReferralPointSplit.ProcessorShare);
    }
}
