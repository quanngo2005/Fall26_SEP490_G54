namespace G54.BLL.Common.Extensions;

/// <summary>
/// Business-calendar helpers for date-only fields such as <c>work.planned_end_date</c>. Those dates are entered by
/// users in Vietnam, so "today" is the calendar day at UTC+7. Vietnam has no daylight saving, so a fixed offset is
/// safe and avoids Windows/Linux time zone id differences in containers.
/// </summary>
public static class TimeProviderExtensions
{
    public static readonly TimeSpan BusinessUtcOffset = TimeSpan.FromHours(7);

    /// <summary>The current calendar date at UTC+7.</summary>
    public static DateOnly GetBusinessToday(this TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        return time.GetUtcNow().ToBusinessDate();
    }

    /// <summary>The calendar date of this instant at UTC+7.</summary>
    public static DateOnly ToBusinessDate(this DateTimeOffset instant) =>
        DateOnly.FromDateTime(instant.ToOffset(BusinessUtcOffset).DateTime);

    /// <summary>The instant a UTC+7 calendar day starts; the inverse of <see cref="ToBusinessDate"/>.</summary>
    public static DateTimeOffset ToBusinessDayStart(this DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MinValue), BusinessUtcOffset);
}
