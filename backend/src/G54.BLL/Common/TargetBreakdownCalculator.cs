using G54.BLL.Dtos.TargetAssignment;

namespace G54.BLL.Common;

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

        var (unit, totalUnits) = ToUnits(targetValue, isWholeNumber);

        // Chia đều số đơn vị cho các ngày; phần dư (remainder) cộng thêm 1 đơn vị cho các ngày đầu.
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

        var (unit, totalUnits) = ToUnits(targetValue, isWholeNumber);

        // Gom ngày làm việc theo tháng (key = ngày 1 của tháng), sắp xếp tăng dần.
        var months = workingDays
            .GroupBy(d => new DateOnly(d.Year, d.Month, 1))
            .OrderBy(g => g.Key)
            .Select(g => g.OrderBy(d => d).ToList())
            .ToList();

        // Mỗi tháng nhận phần đơn vị tỉ lệ với số ngày làm việc của tháng (chia nguyên, làm tròn xuống);
        // phần lẻ còn lại (leftover) chia vòng từng đơn vị cho các tháng đầu để tổng kỳ không đổi.
        var monthUnits = months.Select(m => totalUnits * m.Count / workingDays.Count).ToArray();
        var leftover = totalUnits - monthUnits.Sum();
        for (var i = 0; leftover > 0; i = (i + 1) % months.Count, leftover--)
        {
            monthUnits[i]++;
        }

        // Mỗi tháng tiếp tục chia xuống từng ngày; monthUnits[i] * unit đã đúng bội số của unit nên không làm tròn lại.
        return [.. months.SelectMany((days, i) => SplitByDay(monthUnits[i] * unit, days, isWholeNumber))];
    }

    /// <summary>
    /// Quy đổi target sang số nguyên "đơn vị nhỏ nhất" để chia không sai số thập phân.
    /// Số nguyên (hợp đồng, khách hàng...): unit = 1, làm tròn target về 0 chữ số thập phân.
    /// Số thập phân (VNĐ, %...): unit = 0.0001, làm tròn target về 4 chữ số thập phân.
    /// Ví dụ 10.5 với unit 0.0001 → totalUnits = 105000; chia trên số nguyên rồi nhân lại unit.
    /// </summary>
    private static (decimal Unit, long TotalUnits) ToUnits(decimal targetValue, bool isWholeNumber)
    {
        var unit = isWholeNumber ? 1m : 0.0001m;
        var roundedTotal = Math.Round(targetValue, isWholeNumber ? 0 : 4, MidpointRounding.AwayFromZero);
        return (unit, (long)Math.Round(roundedTotal / unit, 0, MidpointRounding.AwayFromZero));
    }

    /// <summary>Aggregates a day split into MONTH, WEEK (ISO, Monday start) or DAY buckets.</summary>
    public static IReadOnlyList<TargetBreakdownItemDto> Aggregate(
        IReadOnlyList<(DateOnly Day, decimal Value)> daySplit, string granularity)
    {
        if (daySplit.Count == 0)
        {
            return [];
        }

        // Chọn key gom nhóm theo granularity: DAY = chính ngày đó, WEEK = thứ Hai đầu tuần ISO,
        // MONTH (mặc định) = ngày 1 của tháng. Key ép về object để 3 nhánh switch cùng kiểu trả về.
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
