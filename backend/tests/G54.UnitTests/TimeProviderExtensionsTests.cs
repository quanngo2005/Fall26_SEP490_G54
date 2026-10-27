using System.Globalization;
using G54.BLL.Common.Extensions;

namespace G54.UnitTests;

public sealed class TimeProviderExtensionsTests
{
    private sealed class FixedTime(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    [Theory]
    [InlineData("2026-10-21T16:59:59+00:00", "2026-10-21")] // 23:59:59 in Vietnam, still the 21st
    [InlineData("2026-10-21T17:00:00+00:00", "2026-10-22")] // 00:00:00 in Vietnam, already the 22nd
    [InlineData("2026-10-21T20:00:00+00:00", "2026-10-22")]
    [InlineData("2026-12-31T17:00:00+00:00", "2027-01-01")] // crosses the year boundary
    public void GetBusinessToday_UsesUtcPlusSeven(string utcNow, string expected)
    {
        var time = new FixedTime(DateTimeOffset.Parse(utcNow, CultureInfo.InvariantCulture));

        Assert.Equal(DateOnly.Parse(expected, CultureInfo.InvariantCulture), time.GetBusinessToday());
    }

    [Fact]
    public void ToBusinessDate_AndToBusinessDayStart_AreInverseAtTheDayBoundary()
    {
        var lastSecond = DateTimeOffset.Parse("2026-10-21T16:59:59+00:00", CultureInfo.InvariantCulture);
        var firstSecond = DateTimeOffset.Parse("2026-10-21T17:00:00+00:00", CultureInfo.InvariantCulture);

        Assert.Equal(new DateOnly(2026, 10, 21), lastSecond.ToBusinessDate());
        Assert.Equal(new DateOnly(2026, 10, 22), firstSecond.ToBusinessDate());
        Assert.Equal(firstSecond, new DateOnly(2026, 10, 22).ToBusinessDayStart());
        Assert.Equal(new DateOnly(2026, 10, 22), new DateOnly(2026, 10, 22).ToBusinessDayStart().ToBusinessDate());
    }

    [Fact]
    public void GetBusinessToday_RejectsNullTimeProvider()
    {
        Assert.Throws<ArgumentNullException>(() => TimeProviderExtensions.GetBusinessToday(null!));
    }
}
