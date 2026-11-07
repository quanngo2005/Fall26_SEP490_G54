using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Common.Workflow;
using G54.BLL.Dtos.Handover;
using G54.DAL;
using G54.DAL.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace G54.BLL.Services;

/// <summary>
/// Hands a work over to a colleague at once, with no approval step (BR-21). An amount above
/// <c>Handover:AmountThreshold</c> (BR-01) is rejected.
/// </summary>
public sealed class HandoverService(
    AppDbContext db,
    IPermissionService permissions,
    IConfiguration configuration,
    INotifier notifier,
    ILogger<HandoverService> logger,
    TimeProvider time) : IHandoverService
{
    /// <summary>Fallback when <c>Handover:AmountThreshold</c> is not configured.</summary>
    private const decimal DefaultAmountThreshold = 500_000_000m;

    private const int MaxReasonLength = 2000;

    /// <summary>Referrer share when none is set: the recipient receives 100%.</summary>
    private const decimal DefaultReferrerSharePercent = 0m;

    private static readonly Action<ILogger, Guid, Guid, Guid, Guid, Exception?> LogHandoverTransferred =
        LoggerMessage.Define<Guid, Guid, Guid, Guid>(
            LogLevel.Information,
            new EventId(1, "HandoverTransferred"),
            "Handover audit: Action=TRANSFER, HandoverId={HandoverId}, WorkId={WorkId}, FromEmployeeId={FromEmployeeId}, ToEmployeeId={ToEmployeeId}");

    public async Task<HandoverDetailDto> CreateHandoverAsync(
        Guid workId,
        Guid userId,
        CreateHandoverRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var caller = await ResolveCallerAsync(userId, cancellationToken);
        var input = Validate(request);
        var isManager = await HasSetSharePermissionAsync(caller, cancellationToken);
        if (request.ReferrerSharePercent is not null && !isManager)
        {
            throw new ForbiddenException("Chỉ quản lý hoặc quản trị viên mới được đặt tỷ lệ chia điểm cho người giới thiệu.");
        }

        var work = await db.Works
            .FirstOrDefaultAsync(w => w.Id == workId, cancellationToken)
            ?? throw new NotFoundException("Công việc", workId);

        // The assignee, or the assignee's direct manager, hands the work over. There is no approval step (BR-21):
        // the recipient must take it, so the work moves at once.
        var from = caller;
        if (work.AssigneeEmployeeId != caller.Id)
        {
            var assignee = await db.Employees.AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == work.AssigneeEmployeeId, cancellationToken);
            if (assignee?.ManagerId != caller.Id || !isManager)
            {
                throw new ForbiddenException("Chỉ người đang được giao công việc hoặc quản lý trực tiếp của họ mới có quyền bàn giao công việc.");
            }

            from = assignee;
        }

        if (!WorkStateMachine.TryGetNextStatus(work.Status, WorkAction.Handover, out var handedOverStatus))
        {
            throw new ConflictException(
                $"Không thể bàn giao công việc đang ở trạng thái {work.Status.ToEnumString()}.",
                ErrorCodes.WorkInvalidState);
        }

        var threshold = configuration.GetValue("Handover:AmountThreshold", DefaultAmountThreshold);
        if (input.Amount > threshold)
        {
            throw new BusinessRuleException(
                ErrorCodes.HandoverAmountExceedsThreshold,
                $"{ErrorCodes.GetDefaultMessage(ErrorCodes.HandoverAmountExceedsThreshold)} Ngưỡng hiện tại: {threshold:0.##}.",
                StatusCodes.Status422UnprocessableEntity);
        }

        if (input.ToEmployeeId == work.AssigneeEmployeeId)
        {
            throw new ValidationException("Không thể bàn giao công việc cho người đang phụ trách nó.");
        }

        var recipient = await db.Employees.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == input.ToEmployeeId, cancellationToken)
            ?? throw new NotFoundException("Nhân viên nhận bàn giao", input.ToEmployeeId);
        if (recipient.Status != EmployeeStatus.Active)
        {
            throw new ValidationException("Nhân viên nhận bàn giao không còn hoạt động.");
        }

        var now = time.GetUtcNow();
        var handover = new TaskHandover
        {
            Id = Guid.NewGuid(),
            WorkId = workId,
            FromEmployeeId = from.Id,
            ToEmployeeId = recipient.Id,
            Reason = input.Reason,
            ReferrerSharePercent = input.ReferrerSharePercent,
            Status = HandoverStatus.Accepted,
            HandoverAt = now,
            AcceptedAt = now,
            UpdatedAt = now,
        };
        db.TaskHandovers.Add(handover);
        work.AssigneeEmployeeId = recipient.Id;
        work.Status = handedOverStatus;
        work.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);

        LogHandoverTransferred(logger, handover.Id, workId, from.Id, recipient.Id, null);
        await notifier.TryPublishAsync(AutomaticNotifications.HandoverTransferred(handover, work, from, recipient, caller.Id), cancellationToken);

        return Map(handover, work, from, recipient);
    }

    private static HandoverDetailDto Map(TaskHandover handover, Work work, Employee from, Employee to) => new(
        handover.Id,
        work.Id,
        work.Code,
        work.Title,
        from.Id,
        from.FullName,
        to.Id,
        to.FullName,
        handover.Reason,
        handover.ReferrerSharePercent,
        handover.Status.ToEnumString(),
        handover.HandoverAt,
        handover.AcceptedAt,
        handover.UpdatedAt);

    private static ValidInput Validate(CreateHandoverRequestDto request)
    {
        var errors = new List<string>();

        if (request.ToEmployeeId is null || request.ToEmployeeId == Guid.Empty)
        {
            errors.Add("Người nhận là bắt buộc.");
        }

        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason))
        {
            errors.Add("Lí do là bắt buộc.");
        }
        else if (reason.Length > MaxReasonLength)
        {
            errors.Add($"Lí do tối đa {MaxReasonLength} ký tự.");
        }

        if (request.Amount is null)
        {
            errors.Add("Số lượng là bắt buộc.");
        }
        else if (request.Amount < 0)
        {
            errors.Add("Số lượng không được âm.");
        }

        var share = request.ReferrerSharePercent ?? DefaultReferrerSharePercent;
        if (share is < 0 or > 100)
        {
            errors.Add("Phần trăm điểm chia sẻ phải nằm trong khoảng 0-100.");
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        return new ValidInput(request.ToEmployeeId!.Value, reason!, request.Amount!.Value, share);
    }

    /// <summary>Manager/Admin (<c>handover:set_share</c>): may choose the referrer share and transfer a subordinate's work.</summary>
    private async Task<bool> HasSetSharePermissionAsync(Employee caller, CancellationToken cancellationToken)
        => caller.Account is not null
            && await permissions.HasPermissionAsync(caller.Account.Id, "handover", "set_share", PermissionScopes.Own, cancellationToken);

    public async Task<IReadOnlyList<HandoverRecipientDto>> GetRecipientsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(userId, cancellationToken);
        return await db.Employees.AsNoTracking()
            .Where(e => e.DepartmentId == caller.DepartmentId
                && e.Id != caller.Id
                && e.Status == EmployeeStatus.Active)
            .OrderBy(e => e.FullName)
            .Select(e => new HandoverRecipientDto(e.Id, e.EmployeeCode, e.FullName, e.JobPosition.Title))
            .ToListAsync(cancellationToken);
    }

    private async Task<Employee> ResolveCallerAsync(Guid userId, CancellationToken cancellationToken)
    {
        var employee = await db.Employees.AsNoTracking()
            .Include(e => e.Account)
            .FirstOrDefaultAsync(e => e.Id == userId || (e.Account != null && e.Account.Id == userId), cancellationToken);

        if (employee is null || employee.Status != EmployeeStatus.Active)
        {
            throw new UnauthorizedException("Không tìm thấy thông tin nhân viên hợp lệ hoặc tài khoản đã bị khóa.");
        }

        return employee;
    }

    private sealed record ValidInput(Guid ToEmployeeId, string Reason, decimal Amount, decimal ReferrerSharePercent);
}
