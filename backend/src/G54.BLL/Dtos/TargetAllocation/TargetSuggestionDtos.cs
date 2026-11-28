namespace G54.BLL.Dtos.TargetAllocation;

// =========================================================================
// Target suggestion engine (docs/plans/TARGET_ALLOCATION_V3_DECISIONS.md, D1/D4/D5/D6/D8/D9).
// POST /target-suggestions/{year}. Read-only: writes nothing, the caller saves the result through the existing
// quarter-split / allocation / employee-allocation PUT endpoints (docs/api/TARGET_ALLOCATION_CONTRACT.md).
// =========================================================================

/// <summary>Wire values for <see cref="SuggestTargetsRequest.Basis"/>.</summary>
public static class SuggestionBasis
{
    public const string TargetLastYear = "TARGET_LAST_YEAR";
    public const string ActualLastYear = "ACTUAL_LAST_YEAR";
}

/// <summary>Wire values for <see cref="SuggestTargetsRequest.Overwrite"/>.</summary>
public static class SuggestionOverwrite
{
    public const string EmptyOnly = "EMPTY_ONLY";
    public const string Unlocked = "UNLOCKED";
}

/// <summary>Wire values for <see cref="SuggestTargetsRequest.Employees"/> (tier 3 only).</summary>
public static class SuggestionEmployeeScope
{
    public const string All = "ALL";
    public const string NewOnly = "NEW_ONLY";
}

/// <summary>Wire values for <see cref="SuggestTargetsRequest.Periods"/>.</summary>
public static class SuggestionPeriodScope
{
    public const string YearPlus4Q = "YEAR_PLUS_4Q";
    public const string YearOnly = "YEAR_ONLY";
    public const string Quarter = "QUARTER";
}

/// <summary>Wire values for <see cref="SuggestionRoundingDto.Mode"/>.</summary>
public static class SuggestionRoundingModeWire
{
    public const string Nearest = "NEAREST";
    public const string Up = "UP";
    public const string Down = "DOWN";
}

/// <summary>Wire values for <see cref="SuggestionRemainderDto.Method"/>.</summary>
public static class SuggestionRemainderMethodWire
{
    public const string LargestRemainder = "LARGEST_REMAINDER";
    public const string Buffer = "BUFFER";
}

/// <summary>Rounding step/mode (D8). <c>MoneyStep</c> defaults to 10,000,000 (VND), <c>CountStep</c> to 1.</summary>
public sealed record SuggestionRoundingDto(
    decimal? MoneyStep,
    decimal? CountStep,
    string? Mode);

/// <summary>Remainder distribution (D1). Default <c>LARGEST_REMAINDER</c>; <c>BUFFER</c> needs <c>BufferUnitId</c>.</summary>
public sealed record SuggestionRemainderDto(
    string Method,
    Guid? BufferUnitId);

/// <summary>
/// A cell's value already on the grid before suggestion, as the frontend has it in its (possibly unsaved) draft —
/// the suggestion reads these instead of hitting the DB a second time so it suggests against what the user is
/// actually looking at. <c>UnitId</c> is a KPI id (tier 1, one quarter row per KPI), a department id (tier 2), or an
/// employee id (tier 3). Optional <c>KpiId</c> disambiguates rows when a multi-KPI tier 2 call passes drafts for
/// multiple KPIs at once. Omit a unit here to let the suggestion treat it as empty.
/// </summary>
public sealed record SuggestionCurrentValueDto(
    Guid UnitId,
    decimal? Year,
    decimal? Q1,
    decimal? Q2,
    decimal? Q3,
    decimal? Q4,
    bool Locked,
    Guid? KpiId = null);

/// <summary>
/// Request body for <c>POST /target-suggestions/{year}</c>. <c>AnchorId</c> is the branch-year id (tier 1/2) or the
/// department-year id (tier 3) — the same anchor the corresponding Phase 2/3 GET/PUT endpoints take.
/// </summary>
public sealed record SuggestTargetsRequest(
    int Tier,
    string Basis,
    Guid AnchorId,
    List<Guid>? KpiIds,
    Guid? DepartmentId,
    string? Employees,
    string? Periods,
    string Overwrite,
    List<SuggestionCurrentValueDto>? CurrentValues,
    SuggestionRoundingDto? Rounding,
    SuggestionRemainderDto? Remainder);

/// <summary>One suggested cell (a KPI's quarter under tier 1, or a department/employee's target under tier 2/3).</summary>
public sealed record SuggestedCellDto(
    Guid UnitId,
    string UnitName,
    decimal Year,
    decimal Q1,
    decimal Q2,
    decimal Q3,
    decimal Q4,
    bool UsedFallbackAverage,
    bool Overwritten,
    bool Locked,
    string? LockReason);

/// <summary>A unit excluded from suggestion entirely (ineligible department/employee, or period-locked with no stored value to keep).</summary>
public sealed record SuggestionLockedUnitDto(
    Guid UnitId,
    string UnitName,
    string Reason);

/// <summary>Suggestion for one KPI. <c>DisabledReason</c> is set (and <c>Cells</c> empty) when the KPI has no usable history at all (D5).</summary>
public sealed record SuggestedKpiDto(
    Guid KpiId,
    string KpiCode,
    string KpiName,
    string Unit,
    string AggregationType,
    List<SuggestedCellDto> Cells,
    List<SuggestionLockedUnitDto> LockedUnits,
    decimal Total,
    bool SumMatches,
    string? DisabledReason,
    List<string> Warnings);

/// <summary>Response of <c>POST /target-suggestions/{year}</c>.</summary>
public sealed record SuggestTargetsResponseDto(
    int Tier,
    string Basis,
    string Overwrite,
    List<SuggestedKpiDto> Kpis,
    List<string> Warnings);
