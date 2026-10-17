using System.ComponentModel.DataAnnotations;

namespace G54.BLL.Dtos.KpiDefinition;

public sealed record ObjectiveKpiSaveDto
{
    [Required]
    public Guid KpiDefinitionId { get; init; }
}
