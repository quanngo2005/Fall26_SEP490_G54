using System.ComponentModel.DataAnnotations;

namespace G54.BLL.Dtos.KpiDefinition;

/// <summary>
/// A Branch Manager's request for a KPI that is missing from the catalog. The KPI carries every required field of a
/// definition (no value is filled in on the manager's behalf); it is created as DRAFT for the System Admin to complete and submit.
/// </summary>
public sealed record KpiRequestDto
{
    [Required]
    public KpiDefinitionSaveDto? Kpi { get; init; }

    /// <summary>Why the KPI is needed; the length limit is the column's.</summary>
    [Required]
    public string RequestNote { get; init; } = string.Empty;
}
