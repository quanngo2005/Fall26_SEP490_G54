namespace G54.BLL.Dtos.Work;

public sealed record WorkDetailDto(
    Guid Id,
    string Code,
    string Title,
    string? Description,
    Guid? WorkTypeId,
    string? WorkTypeName,
    Guid? BusinessCampaignId,
    string? BusinessCampaignName,
    Guid AssigneeEmployeeId,
    string? AssigneeEmployeeName,
    string? AssigneeEmployeeCode,
    string Priority,
    string Status,
    DateOnly? PlannedStartDate,
    DateOnly? PlannedEndDate,
    decimal? EstimatedHours,
    DateTimeOffset UpdatedAt,
    decimal ProgressPercent = 0m);

/// <summary>Body of <c>PATCH /works/{id}/progress</c>.</summary>
public sealed record UpdateWorkProgressDto
{
    public decimal? ProgressPercent { get; init; }
}
