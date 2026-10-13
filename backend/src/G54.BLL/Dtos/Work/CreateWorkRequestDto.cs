using System.ComponentModel.DataAnnotations;

namespace G54.BLL.Dtos.Work;

public sealed record CreateWorkRequestDto(
    [Required]
    [StringLength(255, MinimumLength = 5)]
    string Title,

    [Required]
    Guid WorkTypeId,

    [Required]
    Guid AssigneeEmployeeId,

    string? Priority = "Medium",

    DateOnly? PlannedStartDate = null,

    DateOnly? PlannedEndDate = null,

    [Range(0, 9999.99)]
    decimal? EstimatedHours = null,

    Guid? BusinessCampaignId = null,

    [StringLength(2000)]
    string? Description = null,

    IReadOnlyList<string>? ChecklistItems = null);
