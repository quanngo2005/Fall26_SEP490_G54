using G54.BLL.Services;

namespace G54.UnitTests;

public sealed class TargetBreakdownCalculatorTests
{
    private static List<DateOnly> WeekdaysBetween(DateOnly from, DateOnly to)
    {
        var days = new List<DateOnly>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                days.Add(d);
            }
        }

        return days;
    }

    [Fact]
    public void SplitByDay_WholeNumber_PutsTheRemainderOnTheEarliestDays()
    {
        // 64 working days, Q4/2026 example from the Figma note: 50 contracts -> 50 days x1, 14 days x0.
        var days = Enumerable.Range(0, 64).Select(i => new DateOnly(2026, 10, 1).AddDays(i)).ToList();

        var split = TargetBreakdownCalculator.SplitByDay(50m, days, isWholeNumber: true);

        Assert.Equal(64, split.Count);
        Assert.Equal(50, split.Count(d => d.Value == 1m));
        Assert.Equal(14, split.Count(d => d.Value == 0m));
        Assert.All(split.Take(50), d => Assert.Equal(1m, d.Value));
        Assert.All(split.Skip(50), d => Assert.Equal(0m, d.Value));
        Assert.Equal(50m, split.Sum(d => d.Value));
    }

    [Fact]
    public void SplitByDay_EvenWholeNumberDivision_SpreadsEquallyWithNoRemainder()
    {
        var days = Enumerable.Range(0, 64).Select(i => new DateOnly(2026, 10, 1).AddDays(i)).ToList();

        var split = TargetBreakdownCalculator.SplitByDay(6_400_000_000m, days, isWholeNumber: true);

        Assert.All(split, d => Assert.Equal(100_000_000m, d.Value));
        Assert.Equal(6_400_000_000m, split.Sum(d => d.Value));
    }

    [Fact]
    public void SplitByDay_Decimal_RoundsTo4DpAndKeepsTheTotalExact()
    {
        var days = Enumerable.Range(0, 3).Select(i => new DateOnly(2026, 1, 1).AddDays(i)).ToList();

        var split = TargetBreakdownCalculator.SplitByDay(1m, days, isWholeNumber: false);

        Assert.Equal(3, split.Count);
        Assert.Equal(1m, split.Sum(d => d.Value));
        // 1 / 3 = 0.3333 remainder 0.0001 -> first day gets the extra 0.0001.
        Assert.Equal(0.3334m, split[0].Value);
        Assert.Equal(0.3333m, split[1].Value);
        Assert.Equal(0.3333m, split[2].Value);
    }

    [Fact]
    public void SplitByDay_NoWorkingDays_ReturnsEmpty()
    {
        var split = TargetBreakdownCalculator.SplitByDay(100m, [], isWholeNumber: true);

        Assert.Empty(split);
    }

    [Fact]
    public void SplitByMonthThenDay_WholeNumber_SplitsMonthsByWorkingDaysThenFrontLoadsDays()
    {
        // Q4/2026 weekdays: Oct 22, Nov 21, Dec 23 = 66. 30 contracts -> floor 10/9/10 = 29, +1 to October.
        var days = WeekdaysBetween(new DateOnly(2026, 10, 1), new DateOnly(2026, 12, 31));

        var split = TargetBreakdownCalculator.SplitByMonthThenDay(30m, days, isWholeNumber: true);
        var months = TargetBreakdownCalculator.Aggregate(split, "MONTH");

        Assert.Equal([11m, 9m, 10m], months.Select(m => m.Value));
        Assert.Equal([22, 21, 23], months.Select(m => m.WorkingDays));
        var october = split.Where(d => d.Day.Month == 10).ToList();
        Assert.All(october.Take(11), d => Assert.Equal(1m, d.Value));
        Assert.All(october.Skip(11), d => Assert.Equal(0m, d.Value));
        Assert.Equal(30m, split.Sum(d => d.Value));
    }

    [Fact]
    public void SplitByMonthThenDay_Decimal_KeepsMonthsProportionalAndTheTotalExact()
    {
        // Figma SCR-STRAT-03-D1: 5.9 bn over 22 + 16 + 21 = 59 days -> 2.2 / 1.6 / 2.1 bn.
        var days = WeekdaysBetween(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31))
            .Concat(WeekdaysBetween(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 23)).Take(16))
            .Concat(WeekdaysBetween(new DateOnly(2026, 12, 1), new DateOnly(2026, 12, 29)).Take(21))
            .ToList();

        var months = TargetBreakdownCalculator.Aggregate(
            TargetBreakdownCalculator.SplitByMonthThenDay(5_900_000_000m, days, isWholeNumber: false), "MONTH");

        Assert.Equal([2_200_000_000m, 1_600_000_000m, 2_100_000_000m], months.Select(m => m.Value));
    }

    [Fact]
    public void Aggregate_ToMonth_PreservesTheTotalAndGroupsByCalendarMonth()
    {
        var days = WeekdaysBetween(new DateOnly(2026, 10, 1), new DateOnly(2026, 12, 3));
        var split = TargetBreakdownCalculator.SplitByDay(days.Count, days, isWholeNumber: true);

        var items = TargetBreakdownCalculator.Aggregate(split, "MONTH");

        Assert.Equal(3, items.Count); // Oct, Nov, Dec
        Assert.Equal(days.Count, items.Sum(i => i.WorkingDays));
        Assert.Equal((decimal)days.Count, items.Sum(i => i.Value));
        Assert.True(items.SequenceEqual(items.OrderBy(i => i.Start)));
    }

    [Fact]
    public void Aggregate_ToWeek_UsesIsoMondayStartWeeks()
    {
        // 2026-10-01 is a Thursday; its ISO week should start Monday 2026-09-28.
        var days = WeekdaysBetween(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 9));
        var split = TargetBreakdownCalculator.SplitByDay(days.Count, days, isWholeNumber: true);

        var items = TargetBreakdownCalculator.Aggregate(split, "WEEK");

        // Items span only the actual working days present in each ISO week, so only weeks starting
        // mid-period (not the first, clipped to the period start) begin on a Monday.
        Assert.Equal(DayOfWeek.Monday, items[1].Start.DayOfWeek);
        Assert.Equal(days.Count, items.Sum(i => i.WorkingDays));
    }

    [Fact]
    public void Aggregate_ToDay_ReturnsOneItemPerWorkingDay()
    {
        var days = WeekdaysBetween(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 7));
        var split = TargetBreakdownCalculator.SplitByDay(days.Count, days, isWholeNumber: true);

        var items = TargetBreakdownCalculator.Aggregate(split, "DAY");

        Assert.Equal(days.Count, items.Count);
        Assert.All(items, i => Assert.Equal(1, i.WorkingDays));
    }
}
