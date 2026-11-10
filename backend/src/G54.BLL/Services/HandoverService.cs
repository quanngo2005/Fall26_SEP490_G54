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
/// Handover requests. Supports two flows distinguished by role:
/// <list type="bullet">
/// <item>Same role (<c>A -&gt; A1</c>, peer swap): creates a <c>PENDING</c> request, notifies the manager and <c>A1</c>; <c>A1</c> (or manager) accepts/rejects, and rejecting returns the work to <c>A</c>.</item>
/// <item>Different role (<c>A -&gt; B</c>, work transfer): requires <c>Work.ProgressPercent</c> at or above the configured threshold (default 90%), then transfers immediately to a randomly selected <c>B</c> (<c>RANDOM</c>) or places the work in the pool (<c>IN_POOL</c>, <c>ASSIGNED</c>) for the manager to assign; <c>B</c> cannot reject.</item>
/// </list>
/// </summary>
public sealed partial class HandoverService(
    AppDbContext db,
    IPermissionService permissions,
    IConfiguration configuration,
    INotifier notifier,
    ILogger<HandoverService> logger,
    TimeProvider time) : IHandoverService
{
    public const string ModeRandom = "RANDOM";
    public const string ModeAssigned = "ASSIGNED";
    public const string SettingKeyAssignmentMode = "handover.transfer_assignment_mode";
    public const string SettingKeyMinProgressPercent = "handover.transfer_min_progress_percent";

    /// <summary>Fallback when <c>Handover:AmountThreshold</c> is not configured.</summary>
    private const decimal DefaultAmountThreshold = 500_000_000m;

    /// <summary>Fallback minimum progress percentage required for a cross-role transfer (<c>A -&gt; B</c>).</summary>
    private const decimal DefaultTransferMinProgressPercent = 90m;

    private const int MaxReasonLength = 2000;

    /// <summary>Referrer share when none is set: the recipient receives 100%.</summary>
    private const decimal DefaultReferrerSharePercent = 0m;

    private static readonly Action<ILogger, Guid, Guid, Guid, Guid?, Exception?> LogHandoverRequested =
        LoggerMessage.Define<Guid, Guid, Guid, Guid?>(
            LogLevel.Information,
            new EventId(1, "HandoverRequested"),
            "Handover audit: Action=REQUEST, HandoverId={HandoverId}, WorkId={WorkId}, FromEmployeeId={FromEmployeeId}, ToEmployeeId={ToEmployeeId}");

    private static readonly Action<ILogger, string, Guid, Guid, Guid, Guid?, Exception?> LogHandoverDecided =
        LoggerMessage.Define<string, Guid, Guid, Guid, Guid?>(
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

        var managerTransfer = work.AssigneeEmployeeId != caller.Id;
        var from = caller;
        if (managerTransfer)
        {
            var assignee = await db.Employees.AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == work.AssigneeEmployeeId, cancellationToken);
            if (assignee?.ManagerId != caller.Id || !isManager)
            {
                throw new ForbiddenException("Chỉ người đang được giao công việc hoặc quản lý trực tiếp của họ mới có quyền bàn giao công việc.");
            }

            if (input.ToEmployeeId is null)
            {
                throw new ValidationException("Người nhận là bắt buộc.");
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

        Employee? recipient = null;
        if (input.ToEmployeeId is { } recipientId)
        {
            recipient = await db.Employees.AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == recipientId, cancellationToken)
                ?? throw new NotFoundException("Nhân viên nhận bàn giao", recipientId);
            if (recipient.Status != EmployeeStatus.Active)
            {
                throw new ValidationException("Nhân viên nhận bàn giao không còn hoạt động.");
            }
        }

        var hasPending = await db.TaskHandovers.AnyAsync(
            h => h.WorkId == workId && (h.Status == HandoverStatus.Pending || h.Status == HandoverStatus.InPool),
            cancellationToken);
        if (hasPending)
        {
            throw new ConflictException(
                ErrorCodes.GetDefaultMessage(ErrorCodes.HandoverAlreadyPending),
                ErrorCodes.HandoverAlreadyPending);
        }

        if (!managerTransfer)
        {
            var (isCrossRoleTransfer, targetRoles) = await ResolveFlowAsync(from.Id, recipient, input.TargetRoleCode, cancellationToken);
            if (isCrossRoleTransfer)
            {
                return await ExecuteCrossRoleTransferAsync(work, from, caller.Id, recipient, targetRoles, input, handedOverStatus, cancellationToken);
            }
        }

        if (recipient is null)
        {
            throw new ValidationException("Người nhận là bắt buộc.");
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
            Status = managerTransfer ? HandoverStatus.Accepted : HandoverStatus.Pending,
            HandoverAt = now,
            AcceptedAt = managerTransfer ? now : null,
            ApprovedBy = managerTransfer ? caller.Id : null,
            UpdatedAt = now,
        };
        db.TaskHandovers.Add(handover);
        if (managerTransfer)
        {
            work.AssigneeEmployeeId = recipient.Id;
            work.Status = handedOverStatus;
            work.UpdatedAt = now;
        }

        await SaveNewHandoverAsync(cancellationToken);

        if (managerTransfer)
        {
            LogHandoverDecided(logger, "TRANSFER", handover.Id, workId, from.Id, recipient.Id, null);
            await notifier.TryPublishAsync(AutomaticNotifications.HandoverDecided(handover, work, from, recipient, caller.Id), cancellationToken);
        }
        else
        {
            LogHandoverRequested(logger, handover.Id, workId, from.Id, recipient.Id, null);
            await notifier.TryPublishAsync(AutomaticNotifications.HandoverRequested(handover, work, from, recipient), cancellationToken);
        }

        return Map(handover, work, from, recipient);
    }

    private async Task<(bool IsCrossRoleTransfer, HashSet<string> TargetRoles)> ResolveFlowAsync(
        Guid fromEmployeeId,
        Employee? recipient,
        string? targetRoleCode,
        CancellationToken cancellationToken)
    {
        var fromRoles = await GetActiveRoleCodesAsync(fromEmployeeId, cancellationToken);

        if (!string.IsNullOrWhiteSpace(targetRoleCode))
        {
            var normalizedRole = targetRoleCode.Trim().ToUpperInvariant();
            var roleExists = await db.Roles.AsNoTracking()
                .AnyAsync(r => r.Code == normalizedRole && r.Status == RecordStatus.Active, cancellationToken);
            if (!roleExists)
            {
                throw new ValidationException("Vai trò nhận chuyển việc không hợp lệ.");
            }

            var targetRoles = new HashSet<string>([normalizedRole], StringComparer.OrdinalIgnoreCase);
            return (!fromRoles.SetEquals(targetRoles), targetRoles);
        }

        if (recipient is null)
        {
            return (false, []);
        }

        var recipientRoles = await GetActiveRoleCodesAsync(recipient.Id, cancellationToken);
        return (!fromRoles.SetEquals(recipientRoles), recipientRoles);
    }

    private async Task<HashSet<string>> GetActiveRoleCodesAsync(Guid employeeId, CancellationToken cancellationToken)
    {
        var codes = await db.UserRoles.AsNoTracking()
            .Where(ur => ur.Account.EmployeeId == employeeId && ur.Role.Status == RecordStatus.Active)
            .Select(ur => ur.Role.Code)
            .ToListAsync(cancellationToken);

        return new HashSet<string>(codes, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<HandoverDetailDto> ExecuteCrossRoleTransferAsync(
        Work work,
        Employee from,
        Guid callerId,
        Employee? explicitRecipient,
        HashSet<string> targetRoles,
        ValidInput input,
        WorkStatus handedOverStatus,
        CancellationToken cancellationToken)
    {
        var settings = await GetSettingsAsync(cancellationToken);
        if (work.ProgressPercent < settings.TransferMinProgressPercent)
        {
            throw new BusinessRuleException(
                ErrorCodes.HandoverProgressBelowThreshold,
                $"{ErrorCodes.GetDefaultMessage(ErrorCodes.HandoverProgressBelowThreshold)} Tiến độ hiện tại: {work.ProgressPercent:0.##}%, ngưỡng tối thiểu: {settings.TransferMinProgressPercent:0.##}%.",
                StatusCodes.Status422UnprocessableEntity);
        }

        var now = time.GetUtcNow();
        if (string.Equals(settings.TransferAssignmentMode, ModeAssigned, StringComparison.OrdinalIgnoreCase))
        {
            var pooled = new TaskHandover
            {
                Id = Guid.NewGuid(),
                WorkId = work.Id,
                FromEmployeeId = from.Id,
                ToEmployeeId = null,
                Reason = input.Reason,
                ReferrerSharePercent = input.ReferrerSharePercent,
                Status = HandoverStatus.InPool,
                HandoverAt = now,
                AcceptedAt = null,
                ApprovedBy = null,
                UpdatedAt = now,
            };
            db.TaskHandovers.Add(pooled);
            await SaveNewHandoverAsync(cancellationToken);

            LogHandoverRequested(logger, pooled.Id, work.Id, from.Id, null, null);
            await notifier.TryPublishAsync(AutomaticNotifications.HandoverPooled(pooled, work, from), cancellationToken);

            return Map(pooled, work, from, null);
        }

        var chosenRecipient = await SelectRandomRecipientAsync(from, explicitRecipient, targetRoles, cancellationToken);
        var transferred = new TaskHandover
        {
            Id = Guid.NewGuid(),
            WorkId = work.Id,
            FromEmployeeId = from.Id,
            ToEmployeeId = chosenRecipient.Id,
            Reason = input.Reason,
            ReferrerSharePercent = input.ReferrerSharePercent,
            Status = HandoverStatus.Accepted,
            HandoverAt = now,
            AcceptedAt = now,
            ApprovedBy = null,
            UpdatedAt = now,
        };
        db.TaskHandovers.Add(transferred);
        work.AssigneeEmployeeId = chosenRecipient.Id;
        work.Status = handedOverStatus;
        work.UpdatedAt = now;

        await SaveNewHandoverAsync(cancellationToken);

        LogHandoverDecided(logger, "TRANSFER", transferred.Id, work.Id, from.Id, chosenRecipient.Id, null);
        await notifier.TryPublishAsync(AutomaticNotifications.HandoverDecided(transferred, work, from, chosenRecipient, callerId), cancellationToken);

        return Map(transferred, work, from, chosenRecipient);
    }

    private async Task<Employee> SelectRandomRecipientAsync(
        Employee from,
        Employee? explicitRecipient,
        HashSet<string> targetRoles,
        CancellationToken cancellationToken)
    {
        if (targetRoles.Count > 0)
        {
            var upperRoles = targetRoles.Select(r => r.ToUpperInvariant()).ToList();
            var deptCandidates = await db.Employees.AsNoTracking()
                .Where(e => e.DepartmentId == from.DepartmentId
                    && e.Id != from.Id
                    && e.Status == EmployeeStatus.Active
                    && e.Account != null
                    && e.Account.UserRoles.Any(ur => ur.Role.Status == RecordStatus.Active && upperRoles.Contains(ur.Role.Code)))
                .ToListAsync(cancellationToken);

            if (deptCandidates.Count > 0)
            {
                return deptCandidates[Random.Shared.Next(deptCandidates.Count)];
            }

            var allCandidates = await db.Employees.AsNoTracking()
                .Where(e => e.Id != from.Id
                    && e.Status == EmployeeStatus.Active
                    && e.Account != null
                    && e.Account.UserRoles.Any(ur => ur.Role.Status == RecordStatus.Active && upperRoles.Contains(ur.Role.Code)))
                .ToListAsync(cancellationToken);

            if (allCandidates.Count > 0)
            {
                return allCandidates[Random.Shared.Next(allCandidates.Count)];
            }
        }

        if (explicitRecipient is not null)
        {
            return explicitRecipient;
        }

        throw new BusinessRuleException(
            ErrorCodes.HandoverNoEligibleRecipient,
            ErrorCodes.GetDefaultMessage(ErrorCodes.HandoverNoEligibleRecipient),
            StatusCodes.Status422UnprocessableEntity);
    }

    private async Task SaveNewHandoverAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // A concurrent request created the pending/pooled handover between the check above and this insert.
            throw new ConflictException(
                ErrorCodes.GetDefaultMessage(ErrorCodes.HandoverAlreadyPending),
                ErrorCodes.HandoverAlreadyPending);
        }
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
        await notifier.TryPublishAsync(AutomaticNotifications.HandoverDecided(handover, work, handover.FromEmployee, handover.ToEmployee, caller.Id), cancellationToken);

        return Map(handover, work, handover.FromEmployee, handover.ToEmployee);
    }

    /// <summary>Accepts a pending or pooled handover: the work moves to the recipient and becomes HANDED_OVER.</summary>
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
        work.AssigneeEmployeeId = handover.ToEmployeeId!.Value;
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

    private static HandoverDetailDto Map(TaskHandover handover, Work work, Employee from, Employee? to) => new(
        handover.Id,
        work.Id,
        work.Code,
        work.Title,
        from.Id,
        from.FullName,
        to?.Id ?? handover.ToEmployeeId,
        to?.FullName,
        handover.Reason,
        handover.ReferrerSharePercent,
        handover.Status.ToEnumString(),
        handover.HandoverAt,
        handover.AcceptedAt,
        handover.UpdatedAt);

    private static ValidInput Validate(CreateHandoverRequestDto request)
    {
        var errors = new List<string>();

        var hasRecipient = request.ToEmployeeId is not null && request.ToEmployeeId != Guid.Empty;
        var hasTargetRole = !string.IsNullOrWhiteSpace(request.TargetRoleCode);
        if (request.ToEmployeeId == Guid.Empty || (!hasRecipient && !hasTargetRole))
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

        return new ValidInput(hasRecipient ? request.ToEmployeeId : null, request.TargetRoleCode?.Trim(), reason!, request.Amount!.Value, share);
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

    private sealed record ValidInput(Guid? ToEmployeeId, string? TargetRoleCode, string Reason, decimal Amount, decimal ReferrerSharePercent);
}
