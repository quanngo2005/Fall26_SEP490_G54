using System.Globalization;
using System.Text.Json;
using G54.BLL.Common;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Campaign;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace G54.BLL.Services;

public sealed partial class CampaignService(
    AppDbContext db,
    ILogger<CampaignService> logger,
    TimeProvider time,
    IOptions<SystemParametersOptions>? parameters = null,
    INotifier? notifier = null) : ICampaignService
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
        var today = time.GetBusinessToday();
        var metrics = new CampaignOverviewMetricsDto(
            totalCampaigns,
            runningCampaigns,
            plannedCampaigns,
            completedCampaigns,
            totalTasksGenerated,
            totalCompletedTasks,
            await CountSlowCampaignsAsync(query, today, cancellationToken));

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
                c.UpdatedAt,
                0m,
                0m,
                0m,
                null,
                null,
                null,
                null,
                null))
            .ToListAsync(cancellationToken);
        items = await AddPaceAsync(items, today, cancellationToken);

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

        var customerTotal = await db.CampaignCustomers.AsNoTracking().CountAsync(c => c.BusinessCampaignId == id, cancellationToken);
        var customerUnassigned = await db.CampaignCustomers.AsNoTracking()
            .CountAsync(c => c.BusinessCampaignId == id && c.AssigneeEmployeeId == null, cancellationToken);
        var customerClosed = await db.CampaignCustomers.AsNoTracking()
            .CountAsync(c => c.BusinessCampaignId == id && c.ContactStatus == CampaignContactStatus.Closed, cancellationToken);

        var metrics = new CampaignMetricsDto(
            totalWorks,
            completedWorks,
            inExecutionWorks,
            pendingWorks,
            progressPercent,
            customerTotal,
            customerUnassigned,
            customerClosed);

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

        // A campaign targets exactly one product (work type): CAMPAIGN_CONTRACT.md §5.5.
        var distinctWorkTypeIds = (request.WorkTypeIds ?? []).Distinct().ToList();
        if (distinctWorkTypeIds.Count != 1)
        {
            throw new ValidationException("Chiến dịch phải có đúng một loại công việc (sản phẩm).");
        }

        var validWorkTypeCount = await db.WorkTypes.CountAsync(
            wt => distinctWorkTypeIds.Contains(wt.Id) && wt.Status == RecordStatus.Active,
            cancellationToken);

        if (validWorkTypeCount != distinctWorkTypeIds.Count)
        {
            throw new ValidationException("Một hoặc nhiều loại công việc không tồn tại hoặc không ở trạng thái hoạt động.");
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

        var creator = await ResolveEmployeeAsync(currentUserId, cancellationToken);
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
            CreatedBy = creator?.Id,
            UpdatedAt = now
        };

        db.BusinessCampaigns.Add(campaign);

        foreach (var wtId in distinctWorkTypeIds)
        {
            db.CampaignWorkTypes.Add(new CampaignWorkType
            {
                Id = Guid.NewGuid(),
                BusinessCampaignId = campaignId,
                WorkTypeId = wtId,
                UpdatedAt = now
            });
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

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (PostgresConflict.IsUniqueViolation(ex))
        {
            // Two requests raced past the duplicate check above; the unique index decides.
            throw new ConflictException($"Mã chiến dịch '{code}' đã tồn tại trong hệ thống.");
        }

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
            // A campaign targets exactly one product (work type): CAMPAIGN_CONTRACT.md §5.5.
            var distinctWorkTypeIds = request.WorkTypeIds.Distinct().ToList();
            if (distinctWorkTypeIds.Count != 1)
            {
                throw new ValidationException("Chiến dịch phải có đúng một loại công việc (sản phẩm).");
            }

            var validCount = await db.WorkTypes.CountAsync(
                wt => distinctWorkTypeIds.Contains(wt.Id) && wt.Status == RecordStatus.Active,
                cancellationToken);

            if (validCount != distinctWorkTypeIds.Count)
            {
                throw new ValidationException("Một hoặc nhiều loại công việc không tồn tại hoặc không ở trạng thái hoạt động.");
            }

            var existingWts = campaign.CampaignWorkTypes.ToList();
            var toRemoveWt = existingWts.Where(cwt => !distinctWorkTypeIds.Contains(cwt.WorkTypeId)).ToList();
            var toAddWt = distinctWorkTypeIds.Where(wtId => !existingWts.Any(cwt => cwt.WorkTypeId == wtId)).ToList();

            if (toRemoveWt.Count > 0 || toAddWt.Count > 0)
            {
                var hasAssignments = await db.CampaignCustomers.AnyAsync(
                        c => c.BusinessCampaignId == id && c.AssigneeEmployeeId != null, cancellationToken)
                    || await db.Works.AnyAsync(w => w.BusinessCampaignId == id, cancellationToken);

                if (hasAssignments)
                {
                    throw new BusinessRuleException(ErrorCodes.CampaignHasAssignments, null, 409);
                }
            }

            foreach (var rem in toRemoveWt) db.CampaignWorkTypes.Remove(rem);
            foreach (var wtId in toAddWt)
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

            if (toRemove.Count > 0)
            {
                var removedIds = toRemove.Select(p => p.EmployeeId).ToList();
                var hasAssignments = await db.CampaignCustomers.AnyAsync(
                        c => c.BusinessCampaignId == id && c.AssigneeEmployeeId != null && removedIds.Contains(c.AssigneeEmployeeId.Value),
                        cancellationToken)
                    || await db.Works.AnyAsync(
                        w => w.BusinessCampaignId == id && removedIds.Contains(w.AssigneeEmployeeId) && WorkStatusGroups.Open.Contains(w.Status),
                        cancellationToken);

                if (hasAssignments)
                {
                    throw new BusinessRuleException(ErrorCodes.CampaignHasAssignments, null, 409);
                }
            }

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
        if (notifier is not null && currentEmployee is not null)
        {
            var rawParticipantIds = campaign.Participants.Select(p => p.EmployeeId).Distinct().ToList();
            var participantIds = await db.Employees.AsNoTracking()
                .Where(e => rawParticipantIds.Contains(e.Id) && e.Status == EmployeeStatus.Active)
                .Select(e => e.Id)
                .ToListAsync(cancellationToken);
            await notifier.TryPublishAsync(
                AutomaticNotifications.CampaignDecided(campaign, currentEmployee, participantIds, decision == "APPROVE", request.Note),
                cancellationToken);
        }

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

        // The campaign already has exactly one product; batch-generated Works must use that same work type
        // (CAMPAIGN_CONTRACT.md §5.5) instead of silently adding a second one.
        var isLinked = await db.CampaignWorkTypes.AnyAsync(
            cwt => cwt.BusinessCampaignId == id && cwt.WorkTypeId == request.WorkTypeId,
            cancellationToken);

        if (!isLinked)
        {
            throw new BusinessRuleException(ErrorCodes.WorkTypeNotInCampaign, null, 400);
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
        // work.code is varchar(50): clip the campaign code so "TSK-<code>-yyyyMMdd-NNNN" always fits, and continue the
        // sequence of earlier batches that day instead of a random suffix, which collided on uq_work_code.
        var codePrefix = CampaignWorkCode.PrefixFor(campaign, time.GetBusinessToday());
        var workCodes = await CampaignWorkCode.NextCodesAsync(db.Works, codePrefix, totalLeads, cancellationToken);
        var exclusivityDays = Math.Max(1, parameters?.Value.CampaignExclusivityDays ?? 7);
        var exclusivityEnd = startDate.AddDays(exclusivityDays);
        var exclusivityNote = $"Khóa độc quyền {exclusivityDays} ngày đến {exclusivityEnd.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}.";

        var assigneeIds = CampaignAllocation.PickAssignees(rule, participantEmployeeIds, totalLeads, loads);

        for (var i = 0; i < totalLeads; i++)
        {
            var assigneeId = assigneeIds[i];

            var lead = i < leads.Count ? leads[i] : null;
            var workCode = workCodes[i];

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
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (PostgresConflict.IsUniqueViolation(ex))
        {
            // A concurrent batch took the same sequence numbers; nothing was saved, so the caller can retry.
            throw new ConflictException("Có một lượt sinh đầu việc khác đang chạy cho chiến dịch này, vui lòng thử lại.");
        }
        LogBatchWorkGenerated(logger, campaign.Id, generatedWorks.Count, rule, null);
        if (notifier is not null)
        {
            var countsByAssignee = generatedWorks
                .GroupBy(w => w.AssigneeEmployeeId)
                .Select(g => (g.Key, g.Count()));
            await notifier.TryPublishAsync(
                AutomaticNotifications.BatchWorksAssigned(campaign, countsByAssignee, creator),
                cancellationToken);
        }

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

    /// <summary>Completes a RUNNING campaign (CAMPAIGN_CONTRACT.md §5.7).</summary>
    public async Task<CampaignDecisionResultDto> CompleteAsync(
        Guid id,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        var campaign = await db.BusinessCampaigns.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException("Chiến dịch", id);

        if (campaign.Status != BusinessCampaignStatus.Running)
        {
            throw new BusinessRuleException(ErrorCodes.CampaignNotCompletable, null, 409);
        }

        return await ApplyLifecycleAsync(campaign, BusinessCampaignStatus.Completed, "Complete", null, currentUserId, cancellationToken);
    }

    /// <summary>Cancels a PLANNED or RUNNING campaign (CAMPAIGN_CONTRACT.md §5.7); a reason is required.</summary>
    public async Task<CampaignDecisionResultDto> CancelAsync(
        Guid id,
        CancelCampaignRequestDto request,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var reason = request.Reason?.Trim();
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ValidationException("Lý do hủy chiến dịch là bắt buộc.");
        }

        if (reason.Length > 1000)
        {
            throw new ValidationException("Lý do hủy chiến dịch không được vượt quá 1000 ký tự.");
        }

        var campaign = await db.BusinessCampaigns.FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new NotFoundException("Chiến dịch", id);

        if (campaign.Status is not (BusinessCampaignStatus.Planned or BusinessCampaignStatus.Running))
        {
            throw new BusinessRuleException(ErrorCodes.CampaignNotCompletable, null, 409);
        }

        return await ApplyLifecycleAsync(campaign, BusinessCampaignStatus.Cancelled, "Cancel", reason, currentUserId, cancellationToken);
    }

    /// <summary>
    /// Shared body of <see cref="CompleteAsync"/> / <see cref="CancelAsync"/>: cancels every open Work of the
    /// campaign (including lead Works found via <c>CampaignCustomers.WorkId</c>) that is not yet SUBMITTED or
    /// later, records a <see cref="WorkActivity"/> per Work and one <see cref="AuditLog"/> entry for the campaign,
    /// and notifies each affected assignee once. Runs in one transaction (pattern from
    /// <c>EvaluationProfileService.ActivateAsync</c>; the in-memory provider used by unit tests has no transactions).
    /// </summary>
    private async Task<CampaignDecisionResultDto> ApplyLifecycleAsync(
        BusinessCampaign campaign,
        BusinessCampaignStatus newStatus,
        string action,
        string? reason,
        Guid currentUserId,
        CancellationToken cancellationToken)
    {
        var actor = await ResolveEmployeeAsync(currentUserId, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        var leadWorkIds = await db.CampaignCustomers
            .Where(c => c.BusinessCampaignId == campaign.Id && c.WorkId != null)
            .Select(c => c.WorkId!.Value)
            .ToListAsync(cancellationToken);

        var worksToCancel = await db.Works
            .Where(w => (w.BusinessCampaignId == campaign.Id || leadWorkIds.Contains(w.Id))
                && (w.Status == WorkStatus.New || w.Status == WorkStatus.InProgress
                    || w.Status == WorkStatus.HandedOver || w.Status == WorkStatus.Overdue))
            .ToListAsync(cancellationToken);

        await using var transaction = await BeginTransactionAsync(cancellationToken);

        var message = action == "Complete"
            ? "Hủy công việc do chiến dịch đã hoàn tất."
            : $"Hủy công việc do chiến dịch bị hủy: {reason}";
        var cancelledCountsByAssignee = new Dictionary<Guid, int>();
        foreach (var work in worksToCancel)
        {
            work.Status = WorkStatus.Cancelled;
            work.UpdatedAt = now;
            db.WorkActivities.Add(new WorkActivity
            {
                Id = Guid.NewGuid(),
                WorkId = work.Id,
                ActorEmployeeId = actor?.Id,
                Action = "STATUS_CHANGED",
                Message = message,
                CreatedAt = now,
            });
            cancelledCountsByAssignee[work.AssigneeEmployeeId] = cancelledCountsByAssignee.GetValueOrDefault(work.AssigneeEmployeeId) + 1;
        }

        campaign.Status = newStatus;
        campaign.UpdatedAt = now;

        db.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            EntityName = "BusinessCampaign",
            EntityId = campaign.Id.ToString(),
            Action = action,
            NewValues = JsonSerializer.Serialize(new { status = newStatus.ToString().ToUpperInvariant(), reason }),
            ActorId = actor?.Id ?? currentUserId,
            OccurredAt = now,
        });

        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        if (notifier is not null)
        {
            var drafts = AutomaticNotifications.CampaignClosed(
                campaign, cancelledCountsByAssignee.Select(kv => (kv.Key, kv.Value)), newStatus == BusinessCampaignStatus.Completed, reason);
            await notifier.TryPublishAsync(drafts, cancellationToken);
        }

        var resultMessage = newStatus == BusinessCampaignStatus.Completed
            ? "Đã hoàn tất chiến dịch thành công."
            : $"Đã hủy chiến dịch. Lý do: {reason}";

        return new CampaignDecisionResultDto(campaign.Id, newStatus.ToString().ToUpperInvariant(), now, resultMessage);
    }

    /// <summary>
    /// Reminds the campaign owner once that a RUNNING campaign's end date has passed: targets campaigns with a
    /// non-null <see cref="BusinessCampaign.CreatedBy"/> and no existing <see cref="NotificationType.CampaignEndReminder"/>
    /// notification for that campaign yet. Called by <see cref="WorkOverdueBackgroundService"/> after
    /// <c>IWorkService.MarkOverdueAsync</c>. Returns the number of reminders sent.
    /// </summary>
    public async Task<int> SendEndReminderAsync(DateOnly today, CancellationToken cancellationToken = default)
    {
        var candidates = await db.BusinessCampaigns
            .Where(c => c.Status == BusinessCampaignStatus.Running && c.EndDate < today && c.CreatedBy != null)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return 0;
        }

        var campaignIds = candidates.Select(c => c.Id).ToList();
        var alreadyReminded = await db.Notifications
            .Where(n => n.Type == NotificationType.CampaignEndReminder
                && n.RelatedType == NotificationRelatedType.Campaign
                && n.RelatedId != null
                && campaignIds.Contains(n.RelatedId.Value))
            .Select(n => n.RelatedId!.Value)
            .ToListAsync(cancellationToken);

        var toRemind = candidates.Where(c => !alreadyReminded.Contains(c.Id)).ToList();
        if (toRemind.Count == 0)
        {
            return 0;
        }

        if (notifier is not null)
        {
            var drafts = toRemind.Select(AutomaticNotifications.CampaignEndReminder).ToList();
            await notifier.TryPublishAsync(drafts, cancellationToken);
        }

        return toRemind.Count;
    }

    private async Task<IDbContextTransaction?> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        // The in-memory provider used by unit tests has no transaction support.
        return db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
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
