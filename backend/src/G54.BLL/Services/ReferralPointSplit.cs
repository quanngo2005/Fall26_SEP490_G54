namespace G54.BLL.Services;

/// <summary>
/// Splits the Sales KPI points of a successfully handed-over transaction between the referring employee and the
/// processing (credit officer) employee. The referrer share is stored per handover
/// (<c>task_handover.referrer_share_percent</c>); the processor receives the remainder.
/// </summary>
public static class ReferralPointSplit
{
    /// <summary>Decimal places stored for a work result value (numeric(18,4)).</summary>
    private const int Scale = 4;

    private const decimal PercentBase = 100m;

    /// <summary>
    /// The referrer part is rounded to the stored scale and the processor receives the remainder,
    /// so the two parts always add up to <paramref name="totalPoints"/>.
    /// </summary>
    /// <param name="totalPoints">Points to split; cannot be negative.</param>
    /// <param name="referrerSharePercent">Referrer share as a percentage between 0 and 100.</param>
    public static (decimal Referrer, decimal Processor) Split(decimal totalPoints, decimal referrerSharePercent)
    {
        if (totalPoints < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(totalPoints), "Points to split cannot be negative.");
        }

        if (referrerSharePercent is < 0m or > PercentBase)
        {
            throw new ArgumentOutOfRangeException(
                nameof(referrerSharePercent),
                "Referrer share must be between 0 and 100 percent.");
        }

        var referrer = Math.Round(
            totalPoints * referrerSharePercent / PercentBase,
            Scale,
            MidpointRounding.AwayFromZero);
        return (referrer, totalPoints - referrer);
    }
}
