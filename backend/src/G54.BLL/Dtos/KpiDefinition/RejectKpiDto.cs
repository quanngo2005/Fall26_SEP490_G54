using System.ComponentModel.DataAnnotations;

namespace G54.BLL.Dtos.KpiDefinition;

public sealed record RejectKpiDto
{
    /// <summary>Why the KPI is sent back; required, the length limit is the column's.</summary>
    [Required]
    public string Reason { get; init; } = string.Empty;
}
