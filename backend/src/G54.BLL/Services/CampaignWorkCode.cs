using System.Globalization;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

/// <summary>
/// The <c>work.code</c> prefix shared by <see cref="CampaignService.BatchGenerateWorkAsync"/> (batch generation)
/// and <see cref="MyCampaignService.UpdateContactAsync"/> (closing a single campaign customer). Extracted verbatim
/// from the inline logic that used to live only in <c>BatchGenerateWorkAsync</c>: behaviour (clipped campaign code,
/// <c>yyyyMMdd</c> business day, trailing dash) must stay identical.
/// </summary>
internal static class CampaignWorkCode
{
    /// <summary>
    /// Returns <c>"TSK-&lt;code clipped to 32 chars&gt;-yyyyMMdd-"</c> for <paramref name="today"/>. Callers append
    /// <c>(sequenceStart + i + 1).ToString("D4")</c>, where <c>sequenceStart</c> is the count of existing
    /// <c>work.code</c> rows already starting with this prefix, to continue the day's sequence instead of
    /// colliding on <c>uq_work_code</c>.
    /// </summary>
    public static string PrefixFor(BusinessCampaign campaign, DateOnly today)
    {
        var todayStr = today.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var clippedCode = campaign.Code.Length > 32 ? campaign.Code[..32] : campaign.Code;
        return $"TSK-{clippedCode}-{todayStr}-";
    }

    /// <summary>
    /// Returns <paramref name="count"/> sequential <c>work.code</c> values under <paramref name="prefix"/>: one
    /// <see cref="EntityFrameworkQueryableExtensions.CountAsync{TSource}(IQueryable{TSource},System.Threading.CancellationToken)"/>
    /// finds how many codes already start with the prefix, then the whole batch continues that sequence
    /// (<c>(existingCount + i).ToString("D4")</c>) so concurrent batches on the same day do not collide on
    /// <c>uq_work_code</c>. Used by <see cref="CampaignService.BatchGenerateWorkAsync"/> and allocate (Slice B).
    /// </summary>
    public static async Task<List<string>> NextCodesAsync(
        IQueryable<Work> works,
        string prefix,
        int count,
        CancellationToken cancellationToken)
    {
        var existingCount = await works.CountAsync(w => w.Code.StartsWith(prefix), cancellationToken);
        var codes = new List<string>(count);
        for (var i = 1; i <= count; i++)
        {
            codes.Add($"{prefix}{(existingCount + i).ToString("D4", CultureInfo.InvariantCulture)}");
        }

        return codes;
    }
}
