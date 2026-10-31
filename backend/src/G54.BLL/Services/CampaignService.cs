using System.Globalization;
using System.Security.Cryptography;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Campaign;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace G54.BLL.Services;

public sealed class CampaignService(
    AppDbContext db,
    ILogger<CampaignService> logger,
    TimeProvider time) : ICampaignService
{
    /// <summary>Largest value <c>work.target_value</c> (numeric(18,4)) can hold.</summary>
    private const decimal MaxTargetValue = 9_999_999_999_999m;

    private static readonly Action<ILogger, string, Guid, Exception?> LogCampaignCreated =
        LoggerMessage.Define<string, Guid>(
            LogLevel.Information,
            new EventId(1, "CampaignCreated"),
            "Campaign created: Code={Code}, Id={Id}");

    private static readonly Action<ILogger, Guid, string, Exception?> LogCampaignUpdated =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Information,
            new EventId(2, "CampaignUpdated"),
            "Campaign updated: Id={Id}, Name={Name}");

    private static readonly Action<ILogger, Guid, string, Exception?> LogCampaignDecided =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Information,
            new EventId(3, "CampaignDecided"),
            "Campaign decided: Id={Id}, Decision={Decision}");

    private static readonly Action<ILogger, Guid, int, string, Exception?> LogBatchWorkGenerated =
        LoggerMessage.Define<Guid, int, string>(
            LogLevel.Information,
            new EventId(4, "BatchWorkGenerated"),
            "Batch work generated for Campaign {CampaignId}: Count={Count}, Rule={Rule}");

    public async Task<IReadOnlyList<CampaignObjectiveDto>> GetObjectivesAsync(CancellationToken cancellationToken = default) =>
        await db.BusinessObjectives.AsNoTracking()
            .Where(o => o.Status != BusinessObjectiveStatus.Cancelled)
            .OrderBy(o => o.Code)
            .Select(o => new CampaignObjectiveDto(o.Id, o.Code, o.Name, o.Description))
            .ToListAsync(cancellationToken);

    public async Task<CampaignListOverviewDto> GetCampaignsAsync(
        string? search,
        string? status,
        Guid? objectiveId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var safePage = Math.Max(page, 1);
        var safePageSize = Math.Clamp(pageSize, 1, 100);

        var allCampaigns = db.BusinessCampaigns.AsNoTracking();

        var totalCampaigns = await allCampaigns.CountAsync(cancellationToken);
        var runningCampaigns = await allCampaigns.CountAsync(c => c.Status == BusinessCampaignStatus.Running, cancellationToken);
        var plannedCampaigns = await allCampaigns.CountAsync(c => c.Status == BusinessCampaignStatus.Planned, cancellationToken);
        var completedCampaigns = await allCampaigns.CountAsync(c => c.Status == BusinessCampaignStatus.Completed, cancellationToken);

        var totalTasksGenerated = await db.Works.AsNoTracking().CountAsync(w => w.BusinessCampaignId != null, cancellationToken);
        var totalCompletedTasks = await db.Works.AsNoTracking().CountAsync(
            w => w.BusinessCampaignId != null && WorkStatusGroups.Completed.Contains(w.Status),
            cancellationToken);

        var metrics = new CampaignOverviewMetricsDto(
            totalCampaigns,
            runningCampaigns,
            plannedCampaigns,
            completedCampaigns,
            totalTasksGenerated,
            totalCompletedTasks);

        var query = allCampaigns;

        if (!string.IsNullOrWhiteSpace(search))
        {
            var keyword = search.Trim();
            query = query.Where(c => c.Code.Contains(keyword) || c.Name.Contains(keyword) || (c.Description != null && c.Description.Contains(keyword)));
        }

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<BusinessCampaignStatus>(status.Trim(), true, out var parsedStatus))
        {
            query = query.Where(c => c.Status == parsedStatus);
        }

        if (objectiveId.HasValue)
        {
            query = query.Where(c => c.BusinessObjectiveId == objectiveId.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(c => c.UpdatedAt)
            .ThenBy(c => c.Code)
            .Skip((safePage - 1) * safePageSize)
            .Take(safePageSize)
            .Select(c => new CampaignSummaryDto(
                c.Id,
                c.Code,
                c.Name,
                c.Description,
                c.StartDate,
                c.EndDate,
                c.Status.ToString().ToUpperInvariant(),
                c.BusinessObjectiveId,
                c.BusinessObjective.Code,
                c.BusinessObjective.Name,
                c.Participants.Count,
                c.CampaignWorkTypes.Count,
                c.Works.Count,
                c.Works.Count(w => WorkStatusGroups.Completed.Contains(w.Status)),
                c.UpdatedAt))
            .ToListAsync(cancellationToken);

        return new CampaignListOverviewDto(
            metrics,
            new PagedResult<CampaignSummaryDto>(items, totalCount, safePage, safePageSize));
    }

    public async Task<CampaignDetailDto> GetCampaignByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var campaign = await db.BusinessCampaigns
            .AsNoTracking()
            .Include(c => c.BusinessObjective)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (campaign is null)
        {
            throw new NotFoundException("Chiến dịch", id);
        }

        var workTypes = await db.CampaignWorkTypes
            .AsNoTracking()
            .Where(cwt => cwt.BusinessCampaignId == id)
            .Select(cwt => new CampaignWorkTypeItemDto(
                cwt.WorkTypeId,
                cwt.WorkType.Code,
                cwt.WorkType.Name,
                null,
                cwt.WorkType.Description,
                null,
                null))
            .ToListAsync(cancellationToken);
        var targetUnits = await GetTargetUnitsAsync(workTypes.ConvertAll(w => w.WorkTypeId), cancellationToken);
        workTypes = workTypes.ConvertAll(w => w with { TargetUnit = targetUnits.GetValueOrDefault(w.WorkTypeId) });

        var participants = await db.Participants
            .AsNoTracking()
            .Where(p => p.BusinessCampaignId == id)
            .Select(p => new CampaignParticipantItemDto(
                p.EmployeeId,
                p.Employee.EmployeeCode,
                p.Employee.FullName,
                p.Employee.JobPosition != null ? p.Employee.JobPosition.Title : null,
                p.Employee.Department != null ? p.Employee.Department.Name : null,
                p.BusinessCampaign.Works.Count(w => w.AssigneeEmployeeId == p.EmployeeId),
                p.BusinessCampaign.Works.Count(w => w.AssigneeEmployeeId == p.EmployeeId && WorkStatusGroups.Completed.Contains(w.Status))))
            .ToListAsync(cancellationToken);

        var totalWorks = await db.Works.AsNoTracking().CountAsync(w => w.BusinessCampaignId == id, cancellationToken);
        var completedWorks = await db.Works.AsNoTracking().CountAsync(w => w.BusinessCampaignId == id && WorkStatusGroups.Completed.Contains(w.Status), cancellationToken);
        var inExecutionWorks = await db.Works.AsNoTracking().CountAsync(w => w.BusinessCampaignId == id && WorkStatusGroups.InExecution.Contains(w.Status), cancellationToken);
        var pendingWorks = await db.Works.AsNoTracking().CountAsync(w => w.BusinessCampaignId == id && w.Status == WorkStatus.New, cancellationToken);

        var progressPercent = totalWorks > 0
            ? Math.Round((decimal)completedWorks / totalWorks * 100m, 1)
            : 0m;

        var metrics = new CampaignMetricsDto(
            totalWorks,
            completedWorks,
            inExecutionWorks,
            pendingWorks,
            progressPercent);

        var canGenerateBatchWork = campaign.Status is BusinessCampaignStatus.Running or BusinessCampaignStatus.Planned;
        var canApprove = campaign.Status == BusinessCampaignStatus.Planned;

        return new CampaignDetailDto(
            campaign.Id,
            campaign.Code,
            campaign.Name,
            campaign.Description,
            campaign.StartDate,
            campaign.EndDate,
            campaign.Status.ToString().ToUpperInvariant(),
            new CampaignObjectiveDto(
                campaign.BusinessObjective.Id,
                campaign.BusinessObjective.Code,
                campaign.BusinessObjective.Name,
                campaign.BusinessObjective.Description),
            workTypes,
            participants,
            metrics,
            canGenerateBatchWork,
            canApprove,
            campaign.UpdatedAt);
    }

    public async Task<CampaignDetailDto> CreateCampaignAsync(
        CreateCampaignRequestDto request,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || request.Code.Trim().Length < 3 || request.Code.Trim().Length > 50)
        {
            throw new ValidationException("Mã chiến dịch là bắt buộc và phải từ 3 đến 50 ký tự.");
        }

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length < 3 || request.Name.Trim().Length > 255)
        {
            throw new ValidationException("Tên chiến dịch là bắt buộc và phải từ 3 đến 255 ký tự.");
        }

        if (request.EndDate < request.StartDate)
        {
            throw new ValidationException("Ngày kết thúc chiến dịch không được nhỏ hơn ngày bắt đầu.");
        }

        var objective = await db.BusinessObjectives.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == request.BusinessObjectiveId, cancellationToken);

        if (objective is null)
        {
            throw new NotFoundException("Mục tiêu kinh doanh", request.BusinessObjectiveId);
        }

        if (objective.Status == BusinessObjectiveStatus.Cancelled)
        {
            throw new BusinessRuleException(ErrorCodes.ValidationError, "Không thể gắn chiến dịch vào mục tiêu kinh doanh đã bị hủy.");
        }

        var code = request.Code.Trim().ToUpperInvariant();
        var codeExists = await db.BusinessCampaigns.AnyAsync(c => c.Code == code, cancellationToken);
        if (codeExists)
        {
            throw new ConflictException($"Mã chiến dịch '{code}' đã tồn tại trong hệ thống.");
        }

        if (request.WorkTypeIds is { Count: > 0 })
        {
            var distinctWorkTypeIds = request.WorkTypeIds.Distinct().ToList();
            var validCount = await db.WorkTypes.CountAsync(
                wt => distinctWorkTypeIds.Contains(wt.Id) && wt.Status == RecordStatus.Active,
                cancellationToken);

            if (validCount != distinctWorkTypeIds.Count)
            {
                throw new ValidationException("Một hoặc nhiều loại công việc không tồn tại hoặc không ở trạng thái hoạt động.");
            }
        }

        if (request.ParticipantEmployeeIds is { Count: > 0 })
        {
            var distinctEmployeeIds = request.ParticipantEmployeeIds.Distinct().ToList();
            var activeCount = await db.Employees.CountAsync(
                e => distinctEmployeeIds.Contains(e.Id) && e.Status == EmployeeStatus.Active,
                cancellationToken);

            if (activeCount != distinctEmployeeIds.Count)
            {
                throw new ValidationException("Một hoặc nhiều nhân sự tham gia không tồn tại hoặc không ở trạng thái hoạt động.");
            }
        }

        var campaignId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var campaign = new BusinessCampaign
        {
            Id = campaignId,
            BusinessObjectiveId = request.BusinessObjectiveId,
            Code = code,
            Name = request.Name.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Status = BusinessCampaignStatus.Planned,
            UpdatedAt = now
        };

        db.BusinessCampaigns.Add(campaign);

        if (request.WorkTypeIds is { Count: > 0 })
        {
            foreach (var wtId in request.WorkTypeIds.Distinct())
            {
                db.CampaignWorkTypes.Add(new CampaignWorkType
                {
                    Id = Guid.NewGuid(),
                    BusinessCampaignId = campaignId,
                    WorkTypeId = wtId,
                    UpdatedAt = now
                });
            }
        }

        if (request.ParticipantEmployeeIds is { Count: > 0 })
        {
            foreach (var empId in request.ParticipantEmployeeIds.Distinct())
            {
                db.Participants.Add(new Participant
                {
                    Id = Guid.NewGuid(),
                    BusinessCampaignId = campaignId,
                    EmployeeId = empId,
                    UpdatedAt = now
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        LogCampaignCreated(logger, campaign.Code, campaign.Id, null);

        return await GetCampaignByIdAsync(campaignId, cancellationToken);
    }

    public async Task<CampaignDetailDto> UpdateCampaignAsync(
        Guid id,
        UpdateCampaignRequestDto request,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var campaign = await db.BusinessCampaigns
            .Include(c => c.CampaignWorkTypes)
            .Include(c => c.Participants)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (campaign is null)
        {
            throw new NotFoundException("Chiến dịch", id);
        }

        if (campaign.Status is BusinessCampaignStatus.Completed or BusinessCampaignStatus.Cancelled)
        {
            throw new BusinessRuleException(ErrorCodes.Conflict, "Chiến dịch đã kết thúc hoặc bị hủy, không thể chỉnh sửa.");
        }

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length < 3 || request.Name.Trim().Length > 255)
        {
            throw new ValidationException("Tên chiến dịch là bắt buộc và phải từ 3 đến 255 ký tự.");
        }

        if (request.EndDate < request.StartDate)
        {
            throw new ValidationException("Ngày kết thúc chiến dịch không được nhỏ hơn ngày bắt đầu.");
        }

        var maxWorkEndDate = await db.Works
            .Where(w => w.BusinessCampaignId == id && w.PlannedEndDate.HasValue)
            .MaxAsync(w => w.PlannedEndDate, cancellationToken);

        if (maxWorkEndDate.HasValue && request.EndDate < maxWorkEndDate.Value)
        {
            throw new ValidationException(
                $"Ngày kết thúc chiến dịch ({request.EndDate}) không được nhỏ hơn hạn của các công việc đã sinh ({maxWorkEndDate.Value}).");
        }

        var now = DateTimeOffset.UtcNow;
        campaign.Name = request.Name.Trim();
        campaign.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        campaign.StartDate = request.StartDate;
        campaign.EndDate = request.EndDate;
        campaign.UpdatedAt = now;

        if (request.WorkTypeIds != null)
        {
            var distinctWorkTypeIds = request.WorkTypeIds.Distinct().ToList();
            if (distinctWorkTypeIds.Count > 0)
            {
                var validCount = await db.WorkTypes.CountAsync(
                    wt => distinctWorkTypeIds.Contains(wt.Id) && wt.Status == RecordStatus.Active,
                    cancellationToken);

                if (validCount != distinctWorkTypeIds.Count)
                {
                    throw new ValidationException("Một hoặc nhiều loại công việc không tồn tại hoặc không ở trạng thái hoạt động.");
                }
            }

            var existingWts = campaign.CampaignWorkTypes.ToList();
            var toRemove = existingWts.Where(cwt => !distinctWorkTypeIds.Contains(cwt.WorkTypeId)).ToList();
            var toAdd = distinctWorkTypeIds.Where(wtId => !existingWts.Any(cwt => cwt.WorkTypeId == wtId)).ToList();

            foreach (var rem in toRemove) db.CampaignWorkTypes.Remove(rem);
            foreach (var wtId in toAdd)
            {
                db.CampaignWorkTypes.Add(new CampaignWorkType
                {
                    Id = Guid.NewGuid(),
                    BusinessCampaignId = id,
                    WorkTypeId = wtId,
                    UpdatedAt = now
                });
            }
        }

        if (request.ParticipantEmployeeIds != null)
        {
            var distinctEmployeeIds = request.ParticipantEmployeeIds.Distinct().ToList();
            if (distinctEmployeeIds.Count > 0)
            {
                var activeCount = await db.Employees.CountAsync(
                    e => distinctEmployeeIds.Contains(e.Id) && e.Status == EmployeeStatus.Active,
                    cancellationToken);

                if (activeCount != distinctEmployeeIds.Count)
                {
                    throw new ValidationException("Một hoặc nhiều nhân sự tham gia không tồn tại hoặc không ở trạng thái hoạt động.");
                }
            }

            var existingParts = campaign.Participants.ToList();
            var toRemove = existingParts.Where(p => !distinctEmployeeIds.Contains(p.EmployeeId)).ToList();
            var toAdd = distinctEmployeeIds.Where(empId => !existingParts.Any(p => p.EmployeeId == empId)).ToList();

            foreach (var rem in toRemove) db.Participants.Remove(rem);
            foreach (var empId in toAdd)
            {
                db.Participants.Add(new Participant
                {
                    Id = Guid.NewGuid(),
                    BusinessCampaignId = id,
                    EmployeeId = empId,
                    UpdatedAt = now
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        LogCampaignUpdated(logger, campaign.Id, campaign.Name, null);

        return await GetCampaignByIdAsync(id, cancellationToken);
    }

    public async Task DeleteCampaignAsync(
        Guid id,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var campaign = await db.BusinessCampaigns.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (campaign is null)
        {
            throw new NotFoundException("Chiến dịch", id);
        }

        var hasWorks = await db.Works.AnyAsync(w => w.BusinessCampaignId == id, cancellationToken);
        if (hasWorks)
        {
            throw new BusinessRuleException(
                ErrorCodes.Conflict,
                "Chiến dịch đã có công việc được phân bổ/sinh ra, không thể xóa. Bạn có thể chuyển sang trạng thái Tạm dừng (PAUSED) hoặc Hủy (CANCELLED).");
        }

        if (campaign.Status == BusinessCampaignStatus.Running)
        {
            throw new BusinessRuleException(
                ErrorCodes.Conflict,
                "Chiến dịch đang chạy, không thể xóa trực tiếp. Vui lòng chuyển trạng thái Hủy.");
        }

        var participants = await db.Participants.Where(p => p.BusinessCampaignId == id).ToListAsync(cancellationToken);
        db.Participants.RemoveRange(participants);

        var workTypes = await db.CampaignWorkTypes.Where(cwt => cwt.BusinessCampaignId == id).ToListAsync(cancellationToken);
        db.CampaignWorkTypes.RemoveRange(workTypes);

        db.BusinessCampaigns.Remove(campaign);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<CampaignDecisionResultDto> DecideCampaignAsync(
        Guid id,
        CampaignDecisionRequestDto request,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Decision))
        {
            throw new ValidationException("Quyết định phê duyệt là bắt buộc và phải là APPROVE hoặc REJECT.");
        }

        if (request.ApprovedBudget.HasValue && request.ApprovedBudget.Value < 0)
        {
            throw new ValidationException("Ngân sách phê duyệt không được là số âm.");
        }

        var campaign = await db.BusinessCampaigns
            .Include(c => c.Participants)
            .Include(c => c.BusinessObjective)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (campaign is null)
        {
            throw new NotFoundException("Chiến dịch", id);
        }

        var currentEmployee = await ResolveEmployeeAsync(currentUserId, cancellationToken);
        if (campaign.Participants.Any(p => p.EmployeeId == currentUserId || (currentEmployee != null && p.EmployeeId == currentEmployee.Id)))
        {
            throw new BusinessRuleException(
                ErrorCodes.MakerCannotBeChecker,
                "Nhân sự tham gia thực hiện chiến dịch không được tự phê duyệt kế hoạch chiến dịch.");
        }

        if (campaign.Status != BusinessCampaignStatus.Planned)
        {
            throw new BusinessRuleException(
                ErrorCodes.Conflict,
                "Chỉ có thể phê duyệt hoặc từ chối chiến dịch ở trạng thái Kế hoạch (PLANNED).");
        }

        if (campaign.BusinessObjective != null && campaign.BusinessObjective.Status == BusinessObjectiveStatus.Cancelled)
        {
            throw new BusinessRuleException(
                ErrorCodes.ValidationError,
                "Mục tiêu kinh doanh liên kết đã bị hủy, không thể phê duyệt chiến dịch.");
        }

        var decision = request.Decision.Trim().ToUpperInvariant();
        string message;
        if (decision == "APPROVE")
        {
            var today = time.GetBusinessToday();
            if (campaign.EndDate < today)
            {
                throw new BusinessRuleException(
                    ErrorCodes.ValidationError,
                    "Không thể phê duyệt chiến dịch đã quá hạn kết thúc so với thời gian máy chủ.");
            }

            campaign.Status = BusinessCampaignStatus.Running;
            campaign.UpdatedAt = DateTimeOffset.UtcNow;
            message = string.IsNullOrWhiteSpace(request.Note)
                ? "Đã phê duyệt kế hoạch chiến dịch kinh doanh thành công."
                : $"Đã phê duyệt kế hoạch chiến dịch kinh doanh thành công. Ghi chú: {request.Note.Trim()}";
        }
        else if (decision == "REJECT")
        {
            campaign.Status = BusinessCampaignStatus.Cancelled;
            campaign.UpdatedAt = DateTimeOffset.UtcNow;
            message = string.IsNullOrWhiteSpace(request.Note)
                ? "Đã từ chối kế hoạch chiến dịch kinh doanh."
                : $"Đã từ chối kế hoạch chiến dịch kinh doanh. Lý do: {request.Note.Trim()}";
        }
        else
        {
            throw new ValidationException("Quyết định không hợp lệ. Chỉ chấp nhận APPROVE hoặc REJECT.");
        }

        await db.SaveChangesAsync(cancellationToken);
        LogCampaignDecided(logger, id, decision, null);

        return new CampaignDecisionResultDto(
            campaign.Id,
            campaign.Status.ToString().ToUpperInvariant(),
            campaign.UpdatedAt,
            message);
    }

    public async Task<BatchWorkGenerateResultDto> BatchGenerateWorkAsync(
        Guid id,
        BatchWorkGeneratorRequestDto request,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var campaign = await db.BusinessCampaigns.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (campaign is null)
        {
            throw new NotFoundException("Chiến dịch", id);
        }

        if (campaign.Status is not (BusinessCampaignStatus.Running or BusinessCampaignStatus.Planned))
        {
            throw new BusinessRuleException(
                ErrorCodes.Conflict,
                "Chiến dịch không ở trạng thái Hoạt động (RUNNING) hoặc Kế hoạch (PLANNED), không thể sinh thêm đầu việc.");
        }

        var workType = await db.WorkTypes.AsNoTracking().FirstOrDefaultAsync(wt => wt.Id == request.WorkTypeId, cancellationToken);
        if (workType is null || workType.Status != RecordStatus.Active)
        {
            throw new ValidationException("Loại công việc không tồn tại hoặc không ở trạng thái hoạt động.");
        }

        var isLinked = await db.CampaignWorkTypes.AnyAsync(
            cwt => cwt.BusinessCampaignId == id && cwt.WorkTypeId == request.WorkTypeId,
            cancellationToken);

        if (!isLinked)
        {
            db.CampaignWorkTypes.Add(new CampaignWorkType
            {
                Id = Guid.NewGuid(),
                BusinessCampaignId = id,
                WorkTypeId = request.WorkTypeId,
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        if (request.TotalLeadsCount <= 0 || request.TotalLeadsCount > 1000)
        {
            throw new ValidationException("Số lượng đầu việc cần sinh phải từ 1 đến 1000.");
        }

        if (request.TargetValue is < 0 or > MaxTargetValue)
        {
            throw new ValidationException("Mức mong muốn phải là số không âm và không vượt quá 9.999.999.999.999.");
        }

        var startDate = request.PlannedStartDate ?? campaign.StartDate;
        var endDate = request.PlannedEndDate ?? campaign.EndDate;

        if (endDate < startDate)
        {
            throw new ValidationException("Hạn hoàn thành công việc không được nhỏ hơn ngày bắt đầu.");
        }

        if (endDate > campaign.EndDate)
        {
            throw new ValidationException(
                $"Hạn hoàn thành công việc ({endDate}) không được vượt quá ngày kết thúc chiến dịch ({campaign.EndDate}).");
        }

        var participantQuery = db.Participants
            .Where(p => p.BusinessCampaignId == id)
            .Include(p => p.Employee)
            .Where(p => p.Employee.Status == EmployeeStatus.Active);

        if (request.TargetEmployeeIds is { Count: > 0 })
        {
            participantQuery = participantQuery.Where(p => request.TargetEmployeeIds.Contains(p.EmployeeId));
        }

        var activeParticipants = await participantQuery
            .OrderBy(p => p.Employee.EmployeeCode)
            .ToListAsync(cancellationToken);

        if (activeParticipants.Count == 0)
        {
            throw new BusinessRuleException(
                ErrorCodes.ValidationError,
                "Chiến dịch chưa có nhân sự tham gia hợp lệ ở trạng thái hoạt động để phân bổ đầu việc.");
        }

        var creator = await ResolveEmployeeAsync(currentUserId, cancellationToken)
            ?? throw new ForbiddenException("Không xác định được nhân sự thực hiện thao tác.");

        var priority = ParsePriority(request.Priority);
        var estimatedHours = request.EstimatedHours is > 0 ? request.EstimatedHours.Value : 1.0m;
        var leadPoolSource = string.IsNullOrWhiteSpace(request.LeadPoolSource)
            ? $"Chiến dịch {campaign.Name}"
            : request.LeadPoolSource.Trim();

        var rule = (request.AllocationRule ?? "ROUND_ROBIN").Trim().ToUpperInvariant();

        var participantEmployeeIds = activeParticipants.Select(p => p.EmployeeId).ToList();
        var loads = new Dictionary<Guid, int>();
        if (rule == "LOAD_BALANCING")
        {
            var activeCounts = await db.Works
                .Where(w => participantEmployeeIds.Contains(w.AssigneeEmployeeId) && WorkStatusGroups.ActiveLoad.Contains(w.Status))
                .GroupBy(w => w.AssigneeEmployeeId)
                .Select(g => new { EmployeeId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.EmployeeId, x => x.Count, cancellationToken);

            foreach (var pid in participantEmployeeIds)
            {
                loads[pid] = activeCounts.GetValueOrDefault(pid, 0);
            }
        }

        var leads = request.Leads ?? [];
        var totalLeads = request.TotalLeadsCount;
        var targetUnit = request.TargetValue is null
            ? null
            : (await GetTargetUnitsAsync([request.WorkTypeId], cancellationToken)).GetValueOrDefault(request.WorkTypeId);
        var generatedWorks = new List<Work>(totalLeads);
        var todayStr = time.GetBusinessToday().ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var exclusivityEnd = startDate.AddDays(7);
        var exclusivityNote = $"Khóa độc quyền 7 ngày đến {exclusivityEnd.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}.";

        for (var i = 0; i < totalLeads; i++)
        {
            Guid assigneeId;
            if (rule == "LOAD_BALANCING")
            {
                assigneeId = loads.OrderBy(kv => kv.Value).ThenBy(kv => kv.Key).First().Key;
                loads[assigneeId]++;
            }
            else if (rule == "EQUAL_SPLIT")
            {
                var baseQuota = totalLeads / activeParticipants.Count;
                var remainder = totalLeads % activeParticipants.Count;
                var cumulative = 0;
                var selectedIndex = activeParticipants.Count - 1;

                for (var pIdx = 0; pIdx < activeParticipants.Count; pIdx++)
                {
                    var quota = baseQuota + (pIdx < remainder ? 1 : 0);
                    cumulative += quota;
                    if (i < cumulative)
                    {
                        selectedIndex = pIdx;
                        break;
                    }
                }
                assigneeId = activeParticipants[selectedIndex].EmployeeId;
            }
            else
            {
                // ROUND_ROBIN
                assigneeId = activeParticipants[i % activeParticipants.Count].EmployeeId;
            }

            var lead = i < leads.Count ? leads[i] : null;
            var workCode = $"TSK-{campaign.Code}-{todayStr}-{(i + 1):D4}-{RandomNumberGenerator.GetInt32(100, 999)}";

            string title;
            if (!string.IsNullOrWhiteSpace(request.WorkTitleTemplate))
            {
                title = request.WorkTitleTemplate
                    .Replace("{CampaignName}", campaign.Name)
                    .Replace("{CampaignCode}", campaign.Code)
                    .Replace("{CustomerName}", lead?.CustomerName ?? string.Empty)
                    .Replace("{CustomerCode}", lead?.CustomerCode ?? string.Empty)
                    .Trim();

                if (string.IsNullOrWhiteSpace(title))
                {
                    title = $"[Chiến dịch {campaign.Code}] Khách hàng tiềm năng #{i + 1}";
                }
            }
            else if (lead != null && !string.IsNullOrWhiteSpace(lead.CustomerName))
            {
                title = $"[Chiến dịch {campaign.Code}] Tiếp cận {lead.CustomerName}";
            }
            else
            {
                title = $"[Chiến dịch {campaign.Code}] Chăm sóc khách hàng tiềm năng #{i + 1}";
            }

            string description;
            if (lead != null && (!string.IsNullOrWhiteSpace(lead.CustomerCode) || !string.IsNullOrWhiteSpace(lead.CustomerName) || !string.IsNullOrWhiteSpace(lead.Note)))
            {
                description = $"Nguồn: {leadPoolSource}. Mã KH: {lead.CustomerCode ?? "N/A"}, Tên KH: {lead.CustomerName ?? "N/A"}. Ghi chú: {lead.Note ?? "Không có"}. {exclusivityNote}";
            }
            else
            {
                description = $"Đầu việc được sinh tự động từ nguồn: {leadPoolSource}. {exclusivityNote}";
            }

            var workTarget = SplitTarget(request.TargetValue, totalLeads, i);
            if (workTarget is { } expected)
            {
                description += $" Mức mong muốn: {expected.ToString("0.####", CultureInfo.InvariantCulture)} {targetUnit}".TrimEnd() + ".";
            }

            var work = new Work
            {
                Id = Guid.NewGuid(),
                Code = workCode,
                Title = title,
                Description = description,
                TargetValue = workTarget,
                WorkTypeId = request.WorkTypeId,
                BusinessCampaignId = campaign.Id,
                AssigneeEmployeeId = assigneeId,
                CreatedBy = creator.Id,
                Priority = priority,
                Status = WorkStatus.New,
                EstimatedHours = estimatedHours,
                PlannedStartDate = startDate,
                PlannedEndDate = endDate,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            generatedWorks.Add(work);
        }

        db.Works.AddRange(generatedWorks);
        await db.SaveChangesAsync(cancellationToken);
        LogBatchWorkGenerated(logger, campaign.Id, generatedWorks.Count, rule, null);

        return new BatchWorkGenerateResultDto(
            CampaignId: campaign.Id,
            CampaignCode: campaign.Code,
            TotalGenerated: generatedWorks.Count,
            ParticipantCount: activeParticipants.Count,
            TasksPerParticipantAverage: Math.Round((decimal)generatedWorks.Count / activeParticipants.Count, 1),
            GeneratedTaskCodes: generatedWorks.Select(w => w.Code).Take(50).ToList(),
            LeadPoolSource: leadPoolSource,
            GeneratedAt: DateTimeOffset.UtcNow,
            Message: $"Đã tự động sinh {generatedWorks.Count} đầu việc và phân bổ thành công cho {activeParticipants.Count} cán bộ theo quy tắc {rule}."
        );
    }

    /// <summary>
    /// The unit of the KPI each work type is measured by (accounts, VND billion, ...): the current required KPI first,
    /// otherwise the earliest current one. Work types without a current KPI have no entry.
    /// </summary>
    private async Task<Dictionary<Guid, string>> GetTargetUnitsAsync(
        IReadOnlyCollection<Guid> workTypeIds,
        CancellationToken cancellationToken)
    {
        var today = time.GetBusinessToday();
        var units = await db.WorkTypeKpis.AsNoTracking()
            .Where(k => workTypeIds.Contains(k.WorkTypeId) && k.EffectiveFrom <= today && (k.EffectiveTo == null || k.EffectiveTo >= today))
            .OrderByDescending(k => k.IsRequired).ThenBy(k => k.EffectiveFrom).ThenBy(k => k.Id)
            .Select(k => new { k.WorkTypeId, k.KpiDefinition.Unit })
            .ToListAsync(cancellationToken);
        return units.GroupBy(k => k.WorkTypeId).ToDictionary(g => g.Key, g => g.First().Unit);
    }

    /// <summary>
    /// The share of <paramref name="total"/> for work number <paramref name="index"/> of <paramref name="count"/>: an even
    /// share rounded down to 4 decimals, with the remainder on the last work so the shares add up exactly.
    /// </summary>
    private static decimal? SplitTarget(decimal? total, int count, int index)
    {
        if (total is not { } value)
        {
            return null;
        }

        var share = Math.Round(value / count, 4, MidpointRounding.ToZero);
        return index == count - 1 ? value - (share * (count - 1)) : share;
    }

    private async Task<Employee?> ResolveEmployeeAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await db.Employees
            .AsNoTracking()
            .Include(e => e.Account)
            .FirstOrDefaultAsync(e => e.Id == userId || (e.Account != null && e.Account.Id == userId), cancellationToken);
    }

    private static WorkPriority ParsePriority(string? priority)
    {
        if (string.IsNullOrWhiteSpace(priority))
        {
            return WorkPriority.Medium;
        }

        if (priority.Equals("CRITICAL", StringComparison.OrdinalIgnoreCase) || priority.Equals("P1", StringComparison.OrdinalIgnoreCase))
        {
            return WorkPriority.Critical;
        }

        if (priority.Equals("HIGH", StringComparison.OrdinalIgnoreCase) || priority.Equals("P2", StringComparison.OrdinalIgnoreCase))
        {
            return WorkPriority.High;
        }

        if (priority.Equals("LOW", StringComparison.OrdinalIgnoreCase) || priority.Equals("P4", StringComparison.OrdinalIgnoreCase))
        {
            return WorkPriority.Low;
        }

        return WorkPriority.Medium;
    }
}
