using System.Globalization;
using G54.DAL.Entities;

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
}
