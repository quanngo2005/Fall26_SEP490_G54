namespace G54.BLL.Services;

/// <summary>
/// The participant-picking rules shared by <see cref="CampaignService.BatchGenerateWorkAsync"/> (lead/work
/// generation) and <see cref="CampaignCustomerService.AllocateAsync"/> (customer allocation). Extracted from
/// <see cref="CampaignService.BatchGenerateWorkAsync"/> verbatim: behaviour (order, EQUAL_SPLIT largest-remainder-
/// to-first, LOAD_BALANCING tie-break) must stay identical to what was inline there before this extraction.
/// </summary>
internal static class CampaignAllocation
{
    /// <summary>
    /// Returns <paramref name="count"/> assignee ids drawn from <paramref name="participantIds"/> according to
    /// <paramref name="rule"/> (<c>ROUND_ROBIN</c> | <c>LOAD_BALANCING</c> | <c>EQUAL_SPLIT</c>, case-insensitive;
    /// blank or unrecognized defaults to <c>ROUND_ROBIN</c>). <paramref name="loads"/> is the current workload per
    /// participant id and is only read for <c>LOAD_BALANCING</c> (missing ids default to 0 load); it is never
    /// mutated.
    /// </summary>
    public static IReadOnlyList<Guid> PickAssignees(
        string? rule,
        IReadOnlyList<Guid> participantIds,
        int count,
        IReadOnlyDictionary<Guid, int>? loads = null)
    {
        var result = new List<Guid>(Math.Max(count, 0));
        if (participantIds.Count == 0 || count <= 0)
        {
            return result;
        }

        var normalizedRule = (rule ?? "ROUND_ROBIN").Trim().ToUpperInvariant();

        if (normalizedRule == "LOAD_BALANCING")
        {
            var working = new Dictionary<Guid, int>();
            foreach (var pid in participantIds)
            {
                working[pid] = loads is not null && loads.TryGetValue(pid, out var load) ? load : 0;
            }

            for (var i = 0; i < count; i++)
            {
                var assigneeId = working.OrderBy(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;
                result.Add(assigneeId);
                working[assigneeId]++;
            }
        }
        else if (normalizedRule == "EQUAL_SPLIT")
        {
            var baseQuota = count / participantIds.Count;
            var remainder = count % participantIds.Count;

            for (var i = 0; i < count; i++)
            {
                var cumulative = 0;
                var selectedIndex = participantIds.Count - 1;

                for (var pIdx = 0; pIdx < participantIds.Count; pIdx++)
                {
                    var quota = baseQuota + (pIdx < remainder ? 1 : 0);
                    cumulative += quota;
                    if (i < cumulative)
                    {
                        selectedIndex = pIdx;
                        break;
                    }
                }

                result.Add(participantIds[selectedIndex]);
            }
        }
        else
        {
            // ROUND_ROBIN (also the default for blank/unrecognized rules)
            for (var i = 0; i < count; i++)
            {
                result.Add(participantIds[i % participantIds.Count]);
            }
        }

        return result;
    }
}
