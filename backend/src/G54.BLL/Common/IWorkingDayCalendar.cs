namespace G54.BLL.Common;

/// <summary>
/// Seam for working-day computation used by target breakdown. <see cref="WeekdayWorkingDayCalendar"/> is the
/// only implementation today (Mon-Fri). Public holidays and employee leave are NOT subtracted yet — see
/// docs/api/TARGET_ASSIGNMENT_CONTRACT.md "Working days / holidays / leave" note. A future DB-backed
/// implementation (public_holidays + employee_leaves tables) can replace it without touching callers.
/// </summary>
public interface IWorkingDayCalendar
{
    /// <summary>Working days in [from, to] inclusive. <paramref name="employeeId"/> is accepted for a future
    /// per-employee leave calendar; the current implementation ignores it.</summary>
    Task<IReadOnlyList<DateOnly>> GetWorkingDaysAsync(Guid? employeeId, DateOnly from, DateOnly until, CancellationToken ct = default);
}

/// <summary>Monday-Friday working days. Does not subtract public holidays or employee leave (not modeled yet).</summary>
public sealed class WeekdayWorkingDayCalendar : IWorkingDayCalendar
{
    public Task<IReadOnlyList<DateOnly>> GetWorkingDaysAsync(Guid? employeeId, DateOnly from, DateOnly until, CancellationToken ct = default)
    {
        var days = new List<DateOnly>();
        for (var day = from; day <= until; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                days.Add(day);
            }
        }

        return Task.FromResult<IReadOnlyList<DateOnly>>(days);
    }
}
