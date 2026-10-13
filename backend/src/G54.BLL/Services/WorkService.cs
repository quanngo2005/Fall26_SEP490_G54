using System.Security.Cryptography;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Work;
using G54.DAL.Entities;
using G54.DAL.Uow;
using Microsoft.Extensions.Logging;

namespace G54.BLL.Services;

public sealed class WorkService(
    IUnitOfWork unitOfWork,
    ILogger<WorkService> logger) : IWorkService
{
    private static readonly Action<ILogger, string, Guid, Exception?> LogWorkCreated =
        LoggerMessage.Define<string, Guid>(LogLevel.Information, new EventId(1, "WorkCreated"), "Created work {WorkCode} for assignee {AssigneeId}");

    public async Task<WorkDetailDto> CreateWorkAsync(
        CreateWorkRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length < 5 || request.Title.Trim().Length > 255)
        {
            throw new ValidationException("Tiêu đề công việc là bắt buộc và phải từ 5 đến 255 ký tự.");
        }

        var startDate = request.PlannedStartDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
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
            Priority = priority,
            Status = WorkStatus.New,
            EstimatedHours = request.EstimatedHours,
            PlannedStartDate = startDate,
            PlannedEndDate = request.PlannedEndDate,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        await unitOfWork.Works.AddAsync(work, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        LogWorkCreated(logger, work.Code, work.AssigneeEmployeeId, null);

        return new WorkDetailDto(
            Id: work.Id,
            Code: work.Code,
            Title: work.Title,
            Description: work.Description,
            WorkTypeId: work.WorkTypeId,
            WorkTypeName: workType.Name,
            BusinessCampaignId: campaign?.Id,
            BusinessCampaignName: campaign?.Name,
            AssigneeEmployeeId: assignee.Id,
            AssigneeEmployeeName: assignee.FullName,
            AssigneeEmployeeCode: assignee.EmployeeCode,
            Priority: work.Priority.ToString(),
            Status: work.Status.ToString(),
            PlannedStartDate: work.PlannedStartDate,
            PlannedEndDate: work.PlannedEndDate,
            EstimatedHours: work.EstimatedHours,
            UpdatedAt: work.UpdatedAt);
    }

    public async Task<WorkDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var work = await unitOfWork.Works.GetWithDetailsAsync(id, cancellationToken);
        if (work is null)
        {
            return null;
        }

        return new WorkDetailDto(
            Id: work.Id,
            Code: work.Code,
            Title: work.Title,
            Description: work.Description,
            WorkTypeId: work.WorkTypeId,
            WorkTypeName: work.WorkType?.Name,
            BusinessCampaignId: work.BusinessCampaignId,
            BusinessCampaignName: work.BusinessCampaign?.Name,
            AssigneeEmployeeId: work.AssigneeEmployeeId,
            AssigneeEmployeeName: work.AssigneeEmployee?.FullName,
            AssigneeEmployeeCode: work.AssigneeEmployee?.EmployeeCode,
            Priority: work.Priority.ToString(),
            Status: work.Status.ToString(),
            PlannedStartDate: work.PlannedStartDate,
            PlannedEndDate: work.PlannedEndDate,
            EstimatedHours: work.EstimatedHours,
            UpdatedAt: work.UpdatedAt);
    }

    private async Task<string> GenerateUniqueWorkCodeAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var randomSuffix = RandomNumberGenerator.GetInt32(1000, 9999);
            var candidate = $"WRK-{DateTime.UtcNow:yyyyMMdd}-{randomSuffix}";
            var exists = await unitOfWork.Works.ExistsByCodeAsync(candidate, cancellationToken);
            if (!exists)
            {
                return candidate;
            }
        }

        return $"WRK-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
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
