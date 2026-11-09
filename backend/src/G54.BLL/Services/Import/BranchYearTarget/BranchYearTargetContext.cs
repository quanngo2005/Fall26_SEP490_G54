using G54.DAL.Entities;

namespace G54.BLL.Services.Import.BranchYearTarget;

/// <summary>Everything the Branch-Year Target rules and commit need, loaded once per batch (no N+1).</summary>
public sealed class BranchYearTargetContext
{
    public required Department Branch { get; init; }

    public required EvaluationPeriod YearPeriod { get; init; }

    public required Dictionary<string, KpiDefinition> KpiByCode { get; init; }

    /// <summary>Every KPI referenced either by the file or by an existing Branch-Year row (so "not in file"
    /// KPIs - not necessarily present in <see cref="KpiByCode"/> - can still be named in the preview).</summary>
    public required Dictionary<Guid, KpiDefinition> KpiById { get; init; }

    /// <summary>Current (DRAFT or ACTIVE, not Superseded/Closed) Branch-Year row per KPI for <see cref="YearPeriod"/>.</summary>
    public required Dictionary<Guid, TargetAssignment> CurrentByKpiId { get; init; }

    /// <summary>Current Branch-Year row per KPI for <c>year - 1</c>, used for the V14 comparison warning. Empty
    /// when the previous year has no YEAR period or no rows.</summary>
    public required Dictionary<Guid, TargetAssignment> PreviousYearByKpiId { get; init; }
}
