namespace G54.BLL.Services;

/// <summary>
/// Splits the Sales KPI points of a successfully handed-over transaction between the referring employee and the
/// processing (credit officer) employee: 30% to the referrer, 70% to the processor.
/// </summary>
public static class ReferralPointSplit
{
    /// <summary>Share of the transaction points awarded to the referring employee.</summary>
    public const decimal ReferrerShare = 0.30m;

    /// <summary>Share of the transaction points awarded to the processing employee.</summary>
    public const decimal ProcessorShare = 0.70m;

    /// <summary>Decimal places stored for a work result value (numeric(18,4)).</summary>
    private const int Scale = 4;

    /// <summary>
    /// The referrer part is rounded to the stored scale and the processor receives the remainder,
    /// so the two parts always add up to <paramref name="totalPoints"/>.
    /// </summary>
    public static (decimal Referrer, decimal Processor) Split(decimal totalPoints)
    {
        if (totalPoints < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(totalPoints), "Points to split cannot be negative.");
        }

        var referrer = Math.Round(totalPoints * ReferrerShare, Scale, MidpointRounding.AwayFromZero);
        return (referrer, totalPoints - referrer);
    }
}
