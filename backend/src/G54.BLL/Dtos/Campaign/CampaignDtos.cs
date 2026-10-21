using G54.BLL.Common.Models;
using G54.DAL.Entities;

namespace G54.BLL.Dtos.Campaign;

// =========================================================================
// Campaign Query & Detail DTOs (SCR-CMP-01 & SCR-CMP-02)
// =========================================================================

public sealed record CampaignSummaryDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    DateOnly StartDate,
    DateOnly EndDate,
    string Status,
    Guid BusinessObjectiveId,
    string BusinessObjectiveCode,
    string BusinessObjectiveName,
    int ParticipantsCount,
    int WorkTypesCount,
    int GeneratedWorksCount,
    int CompletedWorksCount,
    DateTimeOffset UpdatedAt);

public sealed record CampaignObjectiveDto(
    Guid Id,
    string Code,
    string Name,
    string? Description);

public sealed record CampaignWorkTypeItemDto(
    Guid WorkTypeId,
    string Code,
    string Name,
    string? Process,
    string? ExpectedResult,
    string? RequiredEvidence);

public sealed record CampaignParticipantItemDto(
    Guid EmployeeId,
    string EmployeeCode,
    string FullName,
    string? JobPositionTitle,
    string? DepartmentName,
    int AssignedTasksCount,
    int CompletedTasksCount);

public sealed record CampaignMetricsDto(
    int TotalWorksCount,
    int CompletedWorksCount,
    int InExecutionWorksCount,
    int PendingWorksCount,
    decimal ProgressPercent);

public sealed record CampaignDetailDto(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    DateOnly StartDate,
    DateOnly EndDate,
    string Status,
    CampaignObjectiveDto BusinessObjective,
    List<CampaignWorkTypeItemDto> WorkTypes,
    List<CampaignParticipantItemDto> Participants,
    CampaignMetricsDto Metrics,
    bool CanGenerateBatchWork,
    bool CanApprove,
    DateTimeOffset UpdatedAt);

public sealed record CampaignOverviewMetricsDto(
    int TotalCampaigns,
    int RunningCampaigns,
    int PlannedCampaigns,
    int CompletedCampaigns,
    int TotalTasksGenerated,
    int TotalCompletedTasks);

public sealed record CampaignListOverviewDto(
    CampaignOverviewMetricsDto Metrics,
    PagedResult<CampaignSummaryDto> Campaigns);

// =========================================================================
// Campaign Command DTOs
// =========================================================================

public sealed record CreateCampaignRequestDto(
    Guid BusinessObjectiveId,
    string Code,
    string Name,
    string? Description,
    DateOnly StartDate,
    DateOnly EndDate,
    List<Guid>? WorkTypeIds = null,
    List<Guid>? ParticipantEmployeeIds = null);

public sealed record UpdateCampaignRequestDto(
    string Name,
    string? Description,
    DateOnly StartDate,
    DateOnly EndDate,
    List<Guid>? WorkTypeIds = null,
    List<Guid>? ParticipantEmployeeIds = null);

public sealed record CampaignDecisionRequestDto(
    string Decision,
    string? Note = null,
    decimal? ApprovedBudget = null);

public sealed record CampaignDecisionResultDto(
    Guid CampaignId,
    string Status,
    DateTimeOffset DecidedAt,
    string Message);

// =========================================================================
// Batch Work Generator DTOs (SCR-CMP-01 - UC24 / UC71)
// =========================================================================

public sealed record BatchLeadItemDto(
    string? CustomerCode,
    string? CustomerName,
    string? Note);

public sealed record BatchWorkGeneratorRequestDto(
    Guid WorkTypeId,
    string LeadPoolSource,
    string AllocationRule,
    int TotalLeadsCount,
    string? WorkTitleTemplate = null,
    decimal? EstimatedHours = 1.0m,
    DateOnly? PlannedStartDate = null,
    DateOnly? PlannedEndDate = null,
    string? Priority = "MEDIUM",
    List<Guid>? TargetEmployeeIds = null,
    List<BatchLeadItemDto>? Leads = null);

public sealed record BatchWorkGenerateResultDto(
    Guid CampaignId,
    string CampaignCode,
    int TotalGenerated,
    int ParticipantCount,
    decimal TasksPerParticipantAverage,
    List<string> GeneratedTaskCodes,
    string LeadPoolSource,
    DateTimeOffset GeneratedAt,
    string Message);
