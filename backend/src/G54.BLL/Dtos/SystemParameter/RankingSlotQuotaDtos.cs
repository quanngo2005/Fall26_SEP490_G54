namespace G54.BLL.Dtos.SystemParameter;

public static class RankingSlotTierCodes
{
    public const string A1 = "A1";
    public const string A2 = "A2";
    public const string A3 = "A3";
    public const string B = "B";
    public const string C = "C";

    public static readonly IReadOnlyList<string> All = [A1, A2, A3, B, C];
}

/// <summary>Headcount cap of one tier. A null headcount means the department has not configured the tier yet.</summary>
public sealed record RankingSlotQuotaDto(string TierCode, int? Headcount);

public sealed record RankingSlotQuotasDto(Guid DepartmentId, IReadOnlyList<RankingSlotQuotaDto> Slots, Guid? PeriodId = null);

public sealed record UpdateRankingSlotQuotasDto(Guid DepartmentId, IReadOnlyList<RankingSlotQuotaDto> Slots, Guid? PeriodId = null);
