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
using Npgsql;

namespace G54.BLL.Services;

/// <summary>
/// Handover requests. A request whose amount is above <c>Handover:AmountThreshold</c> (BR-01) is rejected.
/// </summary>
public sealed partial class HandoverService(
    AppDbContext db,
    IPermissionService permissions,
    IConfiguration configuration,
    ILogger<HandoverService> logger,
    TimeProvider time) : IHandoverService
{
    /// <summary>Fallback when <c>Handover:AmountThreshold</c> is not configured.</summary>
    private const decimal DefaultAmountThreshold = 500_000_000m;

    private const int MaxReasonLength = 2000;

    /// <summary>Referrer share when none is set: the recipient receives 100%.</summary>
    private const decimal DefaultReferrerSharePercent = 0m;

    private static readonly Action<ILogger, Guid, Guid, Guid, Guid, Exception?> LogHandoverRequested =
        LoggerMessage.Define<Guid, Guid, Guid, Guid>(
            LogLevel.Information,
            new EventId(1, "HandoverRequested"),
            "Handover audit: Action=REQUEST, HandoverId={HandoverId}, WorkId={WorkId}, FromEmployeeId={FromEmployeeId}, ToEmployeeId={ToEmployeeId}");

    private static readonly Action<ILogger, string, Guid, Guid, Guid, Guid, Exception?> LogHandoverDecided =
        LoggerMessage.Define<string, Guid, Guid, Guid, Guid>(
            LogLevel.Information,
            new EventId(2, "HandoverDecided"),
            "Handover audit: Action={Action}, HandoverId={HandoverId}, WorkId={WorkId}, FromEmployeeId={FromEmployeeId}, ToEmployeeId={ToEmployeeId}");

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

        // The assignee asks (the recipient must then accept or reject). The assignee's direct manager transfers the
        // work outright: the recipient has no say, so the handover is accepted at once.
        var transfer = work.AssigneeEmployeeId != caller.Id;
        var from = caller;
        if (transfer)
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

        var hasPending = await db.TaskHandovers.AnyAsync(
            h => h.WorkId == workId && h.Status == HandoverStatus.Pending,
            cancellationToken);
        if (hasPending)
        {
            throw new ConflictException(
                ErrorCodes.GetDefaultMessage(ErrorCodes.HandoverAlreadyPending),
                ErrorCodes.HandoverAlreadyPending);
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
            Status = transfer ? HandoverStatus.Accepted : HandoverStatus.Pending,
            HandoverAt = now,
            AcceptedAt = transfer ? now : null,
            ApprovedBy = transfer ? caller.Id : null,
            UpdatedAt = now,
        };
        db.TaskHandovers.Add(handover);
        if (transfer)
        {
            work.AssigneeEmployeeId = recipient.Id;
            work.Status = handedOverStatus;
            work.UpdatedAt = now;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent request created the pending handover between the check above and this insert.
            throw new ConflictException(
                ErrorCodes.GetDefaultMessage(ErrorCodes.HandoverAlreadyPending),
                ErrorCodes.HandoverAlreadyPending);
        }

        if (transfer)
        {
            LogHandoverDecided(logger, "TRANSFER", handover.Id, workId, from.Id, recipient.Id, null);
        }
        else
        {
            LogHandoverRequested(logger, handover.Id, workId, from.Id, recipient.Id, null);
        }

        return Map(handover, work, from, recipient);
    }

    public Task<HandoverDetailDto> AcceptHandoverAsync(Guid handoverId, Guid userId, CancellationToken cancellationToken = default)
        => DecideAsync(handoverId, userId, accept: true, cancellationToken);

    public Task<HandoverDetailDto> RejectHandoverAsync(Guid handoverId, Guid userId, CancellationToken cancellationToken = default)
        => DecideAsync(handoverId, userId, accept: false, cancellationToken);

    private async Task<HandoverDetailDto> DecideAsync(Guid handoverId, Guid userId, bool accept, CancellationToken cancellationToken)
    {
        var caller = await ResolveCallerAsync(userId, cancellationToken);

        var handover = await db.TaskHandovers
            .Include(h => h.Work)
            .Include(h => h.FromEmployee)
            .Include(h => h.ToEmployee)
            .FirstOrDefaultAsync(h => h.Id == handoverId, cancellationToken)
            ?? throw new NotFoundException("Yêu cầu bàn giao", handoverId);

        if (handover.ToEmployeeId != caller.Id)
        {
            throw new ForbiddenException("Chỉ người nhận bàn giao mới có quyền chấp nhận hoặc từ chối yêu cầu này.");
        }

        if (handover.Status != HandoverStatus.Pending)
        {
            throw new ConflictException(
                ErrorCodes.GetDefaultMessage(ErrorCodes.HandoverNotPending),
                ErrorCodes.HandoverNotPending);
        }

        var now = time.GetUtcNow();
        var work = handover.Work;
        if (accept)
        {
            AcceptInto(handover, work, now);
        }
        else
        {
            handover.Status = HandoverStatus.Rejected;
        }

        handover.UpdatedAt = now;
        await SaveDecisionAsync(cancellationToken);

        LogHandoverDecided(logger, accept ? "ACCEPT" : "REJECT", handover.Id, work.Id, handover.FromEmployeeId, handover.ToEmployeeId, null);

        return Map(handover, work, handover.FromEmployee, handover.ToEmployee);
    }

    /// <summary>Accepts a pending handover: the work moves to the recipient and becomes HANDED_OVER.</summary>
    private static void AcceptInto(TaskHandover handover, Work work, DateTimeOffset now)
    {
        if (work.AssigneeEmployeeId != handover.FromEmployeeId
            || !WorkStateMachine.TryGetNextStatus(work.Status, WorkAction.Handover, out var nextStatus))
        {
            throw new ConflictException(
                $"Không thể nhận bàn giao công việc đang ở trạng thái {work.Status.ToEnumString()}.",
                ErrorCodes.WorkInvalidState);
        }

        handover.Status = HandoverStatus.Accepted;
        handover.AcceptedAt = now;
        work.AssigneeEmployeeId = handover.ToEmployeeId;
        work.Status = nextStatus;
        work.UpdatedAt = now;
    }

    private async Task SaveDecisionAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another request decided this handover first.
            throw new ConflictException(
                ErrorCodes.GetDefaultMessage(ErrorCodes.HandoverNotPending),
                ErrorCodes.HandoverNotPending);
        }
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
