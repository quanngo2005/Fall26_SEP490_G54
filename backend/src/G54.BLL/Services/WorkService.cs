using System.Security.Cryptography;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Common.Workflow;
using G54.BLL.Dtos.Work;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace G54.BLL.Services;

public sealed class WorkService(
    IUnitOfWork unitOfWork,
    ILogger<WorkService> logger,
    TimeProvider time) : IWorkService
{
    private const int MaxStatusReasonLength = 500;

    private static readonly string[] ManualTargetNames =
        [WorkStatus.InProgress.ToEnumString(), WorkStatus.Done.ToEnumString(), WorkStatus.Cancelled.ToEnumString()];

    private static readonly Action<ILogger, string, Guid, Exception?> LogWorkCreated =
        LoggerMessage.Define<string, Guid>(LogLevel.Information, new EventId(1, "WorkCreated"), "Created work {WorkCode} for assignee {AssigneeId}");

    private static readonly Action<ILogger, string, Guid, WorkStatus, WorkStatus, Guid?, Exception?> LogWorkStatusChanged =
        LoggerMessage.Define<string, Guid, WorkStatus, WorkStatus, Guid?>(
            LogLevel.Information,
            new EventId(2, "WorkStatusChanged"),
            "Work {WorkCode} ({WorkId}) changed status {FromStatus} -> {ToStatus} by {ActorId}");

    public async Task<WorkDetailDto> CreateWorkAsync(
        CreateWorkRequestDto request,
        Guid currentUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length < 5 || request.Title.Trim().Length > 255)
        {
            throw new ValidationException("Tiêu đề công việc là bắt buộc và phải từ 5 đến 255 ký tự.");
        }

        var startDate = request.PlannedStartDate ?? time.GetBusinessToday();
        if (request.PlannedEndDate.HasValue && request.PlannedEndDate.Value < startDate)
        {
            throw new ValidationException("Hạn hoàn thành công việc không được nhỏ hơn ngày bắt đầu.");
        }

        if (request.EstimatedHours.HasValue && request.EstimatedHours.Value < 0)
        {
            throw new ValidationException("Số giờ dự kiến không được âm.");
        }

        var workType = await unitOfWork.WorkTypes.GetByIdAsync(request.WorkTypeId, cancellationToken);
        if (workType is null || workType.Status != RecordStatus.Active)
        {
            throw new ValidationException("Loại công việc không tồn tại hoặc đã bị vô hiệu hóa.");
        }

        var assignee = await unitOfWork.Employees.GetByIdAsync(request.AssigneeEmployeeId, cancellationToken);
        if (assignee is null || assignee.Status != EmployeeStatus.Active)
        {
            throw new ValidationException("Nhân viên được phân công không tồn tại hoặc không ở trạng thái hoạt động.");
        }

        BusinessCampaign? campaign = null;
        if (request.BusinessCampaignId.HasValue)
        {
            var campaignId = request.BusinessCampaignId.Value;
            campaign = await unitOfWork.BusinessCampaigns.GetByIdAsync(campaignId, cancellationToken);
            if (campaign is null)
            {
                throw new NotFoundException("Chiến dịch", campaignId);
            }

            if (campaign.Status != BusinessCampaignStatus.Running && campaign.Status != BusinessCampaignStatus.Planned)
            {
                throw new BusinessRuleException(
                    ErrorCodes.MSG18,
                    "Chiến dịch được gắn không trong trạng thái hoạt động.");
            }

            if (request.PlannedEndDate.HasValue && request.PlannedEndDate.Value > campaign.EndDate)
            {
                throw new BusinessRuleException(
                    ErrorCodes.MSG18,
                    $"Hạn hoàn thành công việc ({request.PlannedEndDate.Value}) vượt quá ngày kết thúc chiến dịch ({campaign.EndDate}).");
            }

            var isParticipant = await unitOfWork.Participants.AnyAsync(
                p => p.BusinessCampaignId == campaign.Id && p.EmployeeId == request.AssigneeEmployeeId,
                cancellationToken);

            if (!isParticipant)
            {
                throw new BusinessRuleException(
                    ErrorCodes.ValidationError,
                    "Nhân viên được giao việc chưa được đăng ký tham gia (Participant) trong chiến dịch này.");
            }
        }

        var creator = await unitOfWork.Employees.FirstOrDefaultAsync(
            e => e.Id == currentUserId || (e.Account != null && e.Account.Id == currentUserId),
            cancellationToken)
            ?? throw new ForbiddenException("Không xác định được nhân sự khởi tạo công việc.");

        var code = await GenerateUniqueWorkCodeAsync(cancellationToken);
        var priority = ParsePriority(request.Priority);

        var work = new Work
        {
            Id = Guid.NewGuid(),
            Code = code,
            Title = request.Title.Trim(),
            Description = request.Description?.Trim(),
            WorkTypeId = workType.Id,
            BusinessCampaignId = campaign?.Id,
            AssigneeEmployeeId = assignee.Id,
            CreatedBy = creator.Id,
            Priority = priority,
            Status = WorkStatus.New,
            EstimatedHours = request.EstimatedHours,
            PlannedStartDate = startDate,
            PlannedEndDate = request.PlannedEndDate,
            UpdatedAt = time.GetUtcNow(),
        };

        await unitOfWork.Works.AddAsync(work, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        LogWorkCreated(logger, work.Code, assignee.Id, null);

        return MapToDetail(work, workType.Name, campaign?.Name, assignee.FullName, assignee.EmployeeCode);
    }

    public async Task<WorkDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var work = await unitOfWork.Works.GetWithDetailsAsync(id, cancellationToken);
        if (work is null)
        {
            return null;
        }

        return MapToDetail(work, work.WorkType?.Name, work.BusinessCampaign?.Name, work.AssigneeEmployee?.FullName, work.AssigneeEmployee?.EmployeeCode);
    }

    public async Task<WorkDetailDto> ChangeStatusAsync(
        Guid id,
        Guid userId,
        WorkStatusDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var (action, target) = ParseManualAction(dto);

        var work = await unitOfWork.Works.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("Công việc", id);
        var actor = await ResolveActorAsync(userId, cancellationToken);
        await EnsureCanChangeStatusAsync(work, actor, target, cancellationToken);

        if (!WorkStateMachine.TryGetNextStatus(work.Status, action, out var nextStatus))
        {
            throw new ConflictException(
                $"Không thể chuyển công việc từ trạng thái {work.Status.ToEnumString()} sang {target.ToEnumString()}.",
                ErrorCodes.WorkInvalidState);
        }

        var fromStatus = work.Status;
        work.Status = nextStatus;
        work.UpdatedAt = time.GetUtcNow();
        unitOfWork.Works.Update(work);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        LogWorkStatusChanged(logger, work.Code, work.Id, fromStatus, nextStatus, actor.Id, null);

        return (await GetByIdAsync(work.Id, cancellationToken))!;
    }

    public async Task<int> MarkOverdueAsync(DateOnly today, CancellationToken cancellationToken = default)
    {
        var candidates = await unitOfWork.Works.Query(asNoTracking: false)
            .Where(w => WorkStatusGroups.OverdueEligible.Contains(w.Status)
                && w.PlannedEndDate != null
                && w.PlannedEndDate < today)
            .ToListAsync(cancellationToken);

        var now = time.GetUtcNow();
        var marked = 0;
        foreach (var work in candidates)
        {
            if (!WorkStateMachine.TryGetNextStatus(work.Status, WorkAction.MarkOverdue, out var nextStatus))
            {
                continue;
            }

            var fromStatus = work.Status;
            work.Status = nextStatus;
            work.UpdatedAt = now;
            marked++;
            LogWorkStatusChanged(logger, work.Code, work.Id, fromStatus, nextStatus, null, null);
        }

        if (marked > 0)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return marked;
    }

    /// <summary>Maps the requested status to the action it needs; only the manual targets are accepted.</summary>
    private static (WorkAction Action, WorkStatus Target) ParseManualAction(WorkStatusDto dto)
    {
        var target = EnumApiFormat.ParseRequiredEnum<WorkStatus>(NormalizeStatusInput(dto.Status), "status");
        var action = target switch
        {
            WorkStatus.InProgress => WorkAction.Start,
            WorkStatus.Done => WorkAction.Complete,
            WorkStatus.Cancelled => WorkAction.Cancel,
            _ => throw new ValidationException(
                $"Không thể đặt thủ công trạng thái '{target.ToEnumString()}'. Chỉ cho phép {string.Join(", ", ManualTargetNames)}."),
        };

        var reason = dto.Reason?.Trim();
        if (reason is { Length: > MaxStatusReasonLength })
        {
            throw new ValidationException($"Lý do không được vượt quá {MaxStatusReasonLength} ký tự.");
        }

        if (action == WorkAction.Cancel && string.IsNullOrWhiteSpace(reason))
        {
            throw new ValidationException("Lý do là bắt buộc khi hủy công việc.");
        }

        return (action, target);
    }
    private async Task<Employee> ResolveActorAsync(Guid userId, CancellationToken cancellationToken)
    {
        var actor = await unitOfWork.Employees.Query()
            .Include(e => e.Account)
            .FirstOrDefaultAsync(e => e.Id == userId || e.Account!.Id == userId, cancellationToken);

        if (actor is null || actor.Status != EmployeeStatus.Active)
        {
            throw new UnauthorizedException("Không tìm thấy thông tin nhân viên hợp lệ hoặc tài khoản đã bị khóa.");
        }

        return actor;
    }

    /// <summary>The assignee starts work; the assignee's direct manager completes or cancels it.</summary>
    private async Task EnsureCanChangeStatusAsync(
        Work work,
        Employee actor,
        WorkStatus target,
        CancellationToken cancellationToken)
    {
        if (target == WorkStatus.InProgress)
        {
            if (work.AssigneeEmployeeId != actor.Id)
            {
                throw new ForbiddenException("Chỉ người được giao việc mới có quyền bắt đầu công việc.");
            }

            return;
        }

        var assignee = await unitOfWork.Employees.GetByIdAsync(work.AssigneeEmployeeId, cancellationToken);
        if (assignee?.ManagerId != actor.Id)
        {
            throw new ForbiddenException("Chỉ quản lý trực tiếp của người được giao việc mới có quyền hoàn tất hoặc hủy công việc.");
        }
    }

    private static WorkDetailDto MapToDetail(
        Work work,
        string? workTypeName,
        string? campaignName,
        string? assigneeName,
        string? assigneeCode) =>
        new(
            Id: work.Id,
            Code: work.Code,
            Title: work.Title,
            Description: work.Description,
            WorkTypeId: work.WorkTypeId,
            WorkTypeName: workTypeName,
            BusinessCampaignId: work.BusinessCampaignId,
            BusinessCampaignName: campaignName,
            AssigneeEmployeeId: work.AssigneeEmployeeId,
            AssigneeEmployeeName: assigneeName,
            AssigneeEmployeeCode: assigneeCode,
            Priority: work.Priority.ToString(),
            Status: work.Status.ToString(),
            PlannedStartDate: work.PlannedStartDate,
            PlannedEndDate: work.PlannedEndDate,
            EstimatedHours: work.EstimatedHours,
            UpdatedAt: work.UpdatedAt);

    private static string? NormalizeStatusInput(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return status;
        }

        // Accept PascalCase (InProgress) as well as UPPER_SNAKE (IN_PROGRESS).
        var trimmed = status.Trim();
        var snake = string.Concat(trimmed.Select((c, i) =>
            i > 0 && char.IsUpper(c) && char.IsLower(trimmed[i - 1]) ? "_" + c : c.ToString()));
        return snake.ToUpperInvariant();
    }

    private async Task<string> GenerateUniqueWorkCodeAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var randomSuffix = RandomNumberGenerator.GetInt32(1000, 9999);
            var candidate = $"WRK-{time.GetBusinessToday():yyyyMMdd}-{randomSuffix}";
            var exists = await unitOfWork.Works.ExistsByCodeAsync(candidate, cancellationToken);
            if (!exists)
            {
                return candidate;
            }
        }

        return $"WRK-{time.GetBusinessToday():yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
    }

    private static WorkPriority ParsePriority(string? priority)
    {
        if (string.IsNullOrWhiteSpace(priority))
        {
            return WorkPriority.Medium;
        }

        if (priority.Equals("P1", StringComparison.OrdinalIgnoreCase)
            || priority.Equals("Critical", StringComparison.OrdinalIgnoreCase))
        {
            return WorkPriority.Critical;
        }

        if (priority.Equals("P2", StringComparison.OrdinalIgnoreCase)
            || priority.Equals("High", StringComparison.OrdinalIgnoreCase))
        {
            return WorkPriority.High;
        }

        if (priority.Equals("Low", StringComparison.OrdinalIgnoreCase))
        {
            return WorkPriority.Low;
        }

        return WorkPriority.Medium;
    }
}
