using System.ComponentModel.DataAnnotations;

namespace G54.BLL.Dtos.BusinessObjective;

/// <summary>
/// Full replacement for PUT. Length limits and the range of <see cref="PriorityWeight"/> follow the database columns
/// and are checked in the service; the range and the total across objectives are still awaiting confirmation.
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

    public decimal? PriorityWeight { get; init; }
}
