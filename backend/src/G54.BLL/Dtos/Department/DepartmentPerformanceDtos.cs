using NpgsqlTypes;

namespace G54.BLL.Dtos.Department;

// SCR-PRF-02 / SCR-PRF-03 additions to the department overview and members (docs/api/DEPARTMENT_CONTRACT.md).

/// <summary>Which period the dashboards compare the selected one with (query value <c>compare</c>).</summary>
public enum PerformanceCompareMode
{
    [PgName("PREVIOUS")] Previous,
    [PgName("SAME_LAST_YEAR")] SameLastYear,
}

/// <summary>Order of the member list inside each job position (query value <c>sort</c>).</summary>
public enum DepartmentMemberSort
{
    [PgName("CHANGE_DESC")] ChangeDesc,
    [PgName("TOTAL_ASC")] TotalAsc,
    [PgName("CODE")] Code,
}

public sealed record DepartmentTrendPointDto(Guid PeriodId, string PeriodName, decimal? Average);

public sealed record DepartmentGroupWeightDto(Guid JobPositionId, string JobPositionTitle, decimal WeightPercent);

public sealed record DepartmentGroupKpiDto(
    Guid KpiDefinitionId,
    string Code,
    string Name,
    string Unit,
    string Direction,
    decimal TargetValue,
    decimal? ActualValue,
    decimal? AchievementPercent,
    decimal? PreviousAchievementPercent,
    decimal? AchievementChange,
    decimal? CampaignActualValue);

/// <summary>KPIs under one root node of the evaluation profiles; <c>Name</c> is null for KPIs under no root.</summary>
public sealed record DepartmentKpiGroupDto(
    string? Name,
    int DisplayOrder,
    IReadOnlyList<DepartmentGroupWeightDto> Weights,
    IReadOnlyList<DepartmentGroupKpiDto> Kpis);

public sealed record DepartmentPositionGroupDto(
    Guid ProfileItemId,
    string Name,
    int DisplayOrder,
    decimal WeightPercent,
    decimal? Average,
    bool IsWeak);

public sealed record DepartmentPositionDto(
    Guid JobPositionId,
    string JobPositionTitle,
    int EmployeeCount,
    int EvaluatedEmployeeCount,
    decimal? Average,
    decimal? PreviousAverage,
    decimal? ChangeFromPrevious,
    IReadOnlyList<DepartmentPositionGroupDto> Groups);

public sealed record DepartmentWeakestGroupDto(string Name, decimal Score);

public sealed record DepartmentAttentionMemberDto(
    Guid EmployeeId,
    string EmployeeCode,
    string FullName,
    string JobPositionTitle,
    decimal TotalScore,
    decimal? PreviousTotalScore,
    decimal? ChangeFromPrevious,
    DepartmentWeakestGroupDto? WeakestGroup,
    int OverdueWorkCount,
    IReadOnlyList<string> Reasons);

public sealed record DepartmentChildUnitDto(
    Guid Id,
    string Code,
    string Name,
    string LevelType,
    int EmployeeCount,
    int EvaluatedEmployeeCount,
    decimal? Average,
    decimal? PreviousAverage,
    decimal? ChangeFromPrevious);

public sealed record DepartmentMemberGroupDto(
    Guid ProfileItemId,
    string Name,
    int DisplayOrder,
    decimal WeightPercent,
    decimal? Score);

/// <summary>The Excel export of the overview: file content and suggested name.</summary>
public sealed record DepartmentOverviewExportDto(byte[] Content, string FileName);

/// <summary>Reasons an employee appears in <c>attentionMembers</c>.</summary>
public static class AttentionReasons
{
    public const string ScoreDrop = "SCORE_DROP";
    public const string LowScore = "LOW_SCORE";
    public const string OverdueWork = "OVERDUE_WORK";
}
