using System.ComponentModel.DataAnnotations;

namespace G54.BLL.Dtos.BusinessObjective;

/// <summary>
/// Full replacement for PUT. Length limits follow the database columns and are checked in the service.
/// </summary>
public sealed record BusinessObjectiveSaveDto
{
    public Guid? ParentObjectiveId { get; init; }

    [Required]
    public string Code { get; init; } = string.Empty;

    [Required]
    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    [Required]
    public DateOnly? StartDate { get; init; }

    [Required]
    public DateOnly? EndDate { get; init; }
}
