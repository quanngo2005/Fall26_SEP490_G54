using G54.BLL.Services;
using G54.DAL.Entities;
using Xunit;

namespace G54.UnitTests;

/// <summary>
/// The <c>work.code</c> prefix shared by <see cref="CampaignService.BatchGenerateWorkAsync"/> and
/// <see cref="MyCampaignService.UpdateContactAsync"/>.
/// </summary>
public sealed class CampaignWorkCodeTests
{
    [Fact]
    public void PrefixFor_ShortCode_UsesCodeAsIs()
    {
        var campaign = new BusinessCampaign { Code = "CMP-AUTUMN" };
        var prefix = CampaignWorkCode.PrefixFor(campaign, new DateOnly(2026, 10, 20));
        Assert.Equal("TSK-CMP-AUTUMN-20261020-", prefix);
    }

    [Fact]
    public void PrefixFor_LongCode_ClipsTo32Chars()
    {
        var longCode = new string('A', 50);
        var campaign = new BusinessCampaign { Code = longCode };
        var prefix = CampaignWorkCode.PrefixFor(campaign, new DateOnly(2026, 10, 20));
        Assert.Equal($"TSK-{longCode[..32]}-20261020-", prefix);
    }
}
