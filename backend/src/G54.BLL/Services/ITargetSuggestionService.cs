using G54.BLL.Dtos.TargetAllocation;

namespace G54.BLL.Services;

/// <summary>
/// Read-only suggestion engine for Target Allocation v3 (docs/plans/TARGET_ALLOCATION_V3_DECISIONS.md, D1/D4/D5/D6/D8/D9).
/// Computes a suggested split for one tier (branch-&gt;quarters, branch-&gt;departments, or department-&gt;employees) from
/// last year's targets or actuals and returns it for the caller to review and save through the existing
/// quarter-split / allocation / employee-allocation endpoints (docs/api/TARGET_ALLOCATION_CONTRACT.md). Writes nothing.
/// </summary>
public interface ITargetSuggestionService
{
    /// <summary>
    /// Builds a suggestion for the requested tier. Never persists anything. A KPI with no usable history at all
    /// (no previous-year target/actual and no same-group/position fallback) comes back with a non-null
    /// <see cref="SuggestedKpiDto.DisabledReason"/> and no cells (D5).
    /// </summary>
    Task<SuggestTargetsResponseDto> SuggestAsync(int year, SuggestTargetsRequest request, Guid userId, CancellationToken cancellationToken);
}
