using G54.BLL.Dtos.TargetAssignment;

namespace G54.BLL.Services;

/// <summary>
/// Pure, unit-testable split of a period target value across working days, then aggregated to
/// MONTH / WEEK (ISO, Monday start) / DAY buckets. The period total is always preserved exactly;
/// any remainder goes to the earliest working days (matches the Figma note "phần lẻ dồn vào các ngày đầu").
/// </summary>
public static class TargetBreakdownCalculator
{
    /// <summary>
    /// Splits <paramref name="targetValue"/> over <paramref name="workingDays"/> (must be sorted ascending,
    /// no duplicates). When <paramref name="isWholeNumber"/>, the remainder unit is 1 (e.g. contracts);
    /// otherwise it is 0.0001 (4 decimal places).
    /// </summary>
    public static IReadOnlyList<(DateOnly Day, decimal Value)> SplitByDay(
        decimal targetValue, IReadOnlyList<DateOnly> workingDays, bool isWholeNumber)
    {
        if (workingDays.Count == 0)
        {
            return [];
        }

        var unit = isWholeNumber ? 1m : 0.0001m;
        var roundedTotal = isWholeNumber ? Math.Round(targetValue, 0, MidpointRounding.AwayFromZero)
                                          : Math.Round(targetValue, 4, MidpointRounding.AwayFromZero);
        var totalUnits = (long)Math.Round(roundedTotal / unit, 0, MidpointRounding.AwayFromZero);

        var dayCount = workingDays.Count;
        var baseUnitsPerDay = totalUnits / dayCount;
        var remainder = (int)(totalUnits % dayCount);

        var result = new List<(DateOnly Day, decimal Value)>(dayCount);
        for (var i = 0; i < dayCount; i++)
        {
            var units = baseUnitsPerDay + (i < remainder ? 1 : 0);
            result.Add((workingDays[i], units * unit));
        }

        return result;
    }

    /// <summary>
    /// Splits the period target across calendar months in proportion to each month's working days
    /// (SCR-STRAT-03-D1: "Tháng = mục tiêu quý chia theo số ngày đi làm của tháng"), then splits each
    /// month across its days with <see cref="SplitByDay"/>. Leftover units go to the earliest months,
    /// then to the earliest days of each month, so the period total is preserved exactly.
    /// </summary>
    public static IReadOnlyList<(DateOnly Day, decimal Value)> SplitByMonthThenDay(
        decimal targetValue, IReadOnlyList<DateOnly> workingDays, bool isWholeNumber)
    {
        if (workingDays.Count == 0)
        {
            return [];
        }

        var unit = isWholeNumber ? 1m : 0.0001m;
        var roundedTotal = Math.Round(targetValue, isWholeNumber ? 0 : 4, MidpointRounding.AwayFromZero);
        var totalUnits = (long)Math.Round(roundedTotal / unit, 0, MidpointRounding.AwayFromZero);
        var months = workingDays
            .GroupBy(d => new DateOnly(d.Year, d.Month, 1))
            .OrderBy(g => g.Key)
            .Select(g => g.OrderBy(d => d).ToList())
            .ToList();

        var monthUnits = months.Select(m => totalUnits * m.Count / workingDays.Count).ToArray();
        var leftover = totalUnits - monthUnits.Sum();
        for (var i = 0; leftover > 0; i = (i + 1) % months.Count, leftover--)
        {
            monthUnits[i]++;
        }

        return [.. months.SelectMany((days, i) => SplitByDay(monthUnits[i] * unit, days, isWholeNumber))];
    }

    /// <summary>Aggregates a day split into MONTH, WEEK (ISO, Monday start) or DAY buckets.</summary>
    public static IReadOnlyList<TargetBreakdownItemDto> Aggregate(
        IReadOnlyList<(DateOnly Day, decimal Value)> daySplit, string granularity)
    {
        if (daySplit.Count == 0)
        {
            return [];
        }

        IEnumerable<IGrouping<object, (DateOnly Day, decimal Value)>> groups = granularity switch
        {
            "DAY" => daySplit.GroupBy(d => (object)d.Day),
            "WEEK" => daySplit.GroupBy(d => (object)IsoWeekStart(d.Day)),
            _ => daySplit.GroupBy(d => (object)new DateOnly(d.Day.Year, d.Day.Month, 1)),
        };

        return groups
            .Select(g =>
            {
                var days = g.OrderBy(x => x.Day).ToList();
                return new TargetBreakdownItemDto(
                    days[0].Day,
                    days[^1].Day,
                    days.Count,
                    days.Sum(x => x.Value));
            })
            .OrderBy(i => i.Start)
            .ToList();
    }

    /// <summary>Monday of the ISO week containing <paramref name="day"/>.</summary>
    private static DateOnly IsoWeekStart(DateOnly day)
    {
        var diff = ((int)day.DayOfWeek + 6) % 7; // Monday = 0
        return day.AddDays(-diff);
    }
}
