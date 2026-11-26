using G54.BLL.Services;
using Xunit;

namespace G54.UnitTests;

/// <summary>
/// The participant-picking rules extracted from <c>CampaignService.BatchGenerateWorkAsync</c> into
/// <see cref="CampaignAllocation"/> so <c>CampaignCustomerService.AllocateAsync</c> can reuse them.
/// </summary>
public sealed class CampaignAllocationTests
{
    [Fact]
    public void PickAssignees_RoundRobin_AlternatesByModulo()
    {
        var participants = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };

        var result = CampaignAllocation.PickAssignees("ROUND_ROBIN", participants, 7);

        for (var i = 0; i < 7; i++)
        {
            Assert.Equal(participants[i % 3], result[i]);
        }
    }

    [Fact]
    public void PickAssignees_UnknownOrBlankRule_DefaultsToRoundRobin()
    {
        var participants = new[] { Guid.NewGuid(), Guid.NewGuid() };

        var blank = CampaignAllocation.PickAssignees(null, participants, 4);
        var unknown = CampaignAllocation.PickAssignees("NOT_A_RULE", participants, 4);

        Assert.Equal(CampaignAllocation.PickAssignees("ROUND_ROBIN", participants, 4), blank);
        Assert.Equal(CampaignAllocation.PickAssignees("ROUND_ROBIN", participants, 4), unknown);
    }

    [Fact]
    public void PickAssignees_EqualSplit_GivesRemainderToTheFirstParticipants()
    {
        var p1 = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var p2 = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var p3 = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var participants = new[] { p1, p2, p3 };

        // 7 / 3 = 2 r1 -> p1 gets 3, p2 gets 2, p3 gets 2
        var result = CampaignAllocation.PickAssignees("EQUAL_SPLIT", participants, 7);

        Assert.Equal(3, result.Count(a => a == p1));
        Assert.Equal(2, result.Count(a => a == p2));
        Assert.Equal(2, result.Count(a => a == p3));
        // First 3 go to p1, next 2 to p2, next 2 to p3 (contiguous blocks, as in the original inline logic).
        Assert.Equal([p1, p1, p1, p2, p2, p3, p3], result);
    }

    [Fact]
    public void PickAssignees_LoadBalancing_PicksLowestLoadThenTiesBreakByKey()
    {
        var low = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var high = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var participants = new[] { high, low }; // order in the list must not matter
        var loads = new Dictionary<Guid, int> { [low] = 0, [high] = 5 };

        var result = CampaignAllocation.PickAssignees("LOAD_BALANCING", participants, 1, loads);

        Assert.Equal(low, result[0]);
    }

    [Fact]
    public void PickAssignees_LoadBalancing_TieBreaksByEmployeeIdAscending()
    {
        var smallerId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var largerId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var participants = new[] { largerId, smallerId };
        var loads = new Dictionary<Guid, int> { [smallerId] = 0, [largerId] = 0 }; // tied load

        var result = CampaignAllocation.PickAssignees("LOAD_BALANCING", participants, 1, loads);

        Assert.Equal(smallerId, result[0]);
    }

    [Fact]
    public void PickAssignees_LoadBalancing_MissingLoadDefaultsToZero()
    {
        var participants = new[] { Guid.NewGuid(), Guid.NewGuid() };

        // No loads dictionary at all: every participant starts at 0 and the picks still distribute evenly.
        var result = CampaignAllocation.PickAssignees("LOAD_BALANCING", participants, 4, null);

        Assert.Equal(2, result.Count(a => a == participants[0]));
        Assert.Equal(2, result.Count(a => a == participants[1]));
    }

    [Fact]
    public void PickAssignees_NoParticipantsOrZeroCount_ReturnsEmpty()
    {
        Assert.Empty(CampaignAllocation.PickAssignees("ROUND_ROBIN", [], 5));
        Assert.Empty(CampaignAllocation.PickAssignees("ROUND_ROBIN", [Guid.NewGuid()], 0));
    }
}
