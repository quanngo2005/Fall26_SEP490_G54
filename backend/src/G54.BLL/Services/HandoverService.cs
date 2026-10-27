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
public sealed class HandoverService(
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

    public async Task<HandoverDetailDto> CreateHandoverAsync(
        Guid workId,
        Guid userId,
        CreateHandoverRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var caller = await ResolveCallerAsync(userId, cancellationToken);
        var input = Validate(request);
        await EnsureMayDecideShareAsync(request, caller, cancellationToken);

        var work = await db.Works.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == workId, cancellationToken)
            ?? throw new NotFoundException("Công việc", workId);

        if (work.AssigneeEmployeeId != caller.Id)
        {
            throw new ForbiddenException("Chỉ người đang được giao công việc mới có quyền yêu cầu bàn giao.");
        }

        if (!WorkStateMachine.TryGetNextStatus(work.Status, WorkAction.Handover, out _))
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

        if (input.ToEmployeeId == caller.Id)
        {
            throw new ValidationException("Không thể bàn giao công việc cho chính mình.");
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
            FromEmployeeId = caller.Id,
            ToEmployeeId = recipient.Id,
            Reason = input.Reason,
            ReferrerSharePercent = input.ReferrerSharePercent,
            Status = HandoverStatus.Pending,
            HandoverAt = now,
            UpdatedAt = now,
        };
        db.TaskHandovers.Add(handover);
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

        LogHandoverRequested(logger, handover.Id, workId, caller.Id, recipient.Id, null);

        return new HandoverDetailDto(
            handover.Id,
            work.Id,
            work.Code,
            work.Title,
            caller.Id,
            caller.FullName,
            recipient.Id,
            recipient.FullName,
            handover.Reason,
            handover.ReferrerSharePercent,
            handover.Status.ToEnumString(),
            handover.HandoverAt,
            handover.UpdatedAt);
    }

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

    /// <summary>Only Manager/Admin (<c>handover:set_share</c>) may choose the referrer share.</summary>
    private async Task EnsureMayDecideShareAsync(
        CreateHandoverRequestDto request,
        Employee caller,
        CancellationToken cancellationToken)
    {
        if (request.ReferrerSharePercent is null)
        {
            return;
        }

        var allowed = caller.Account is not null
            && await permissions.HasPermissionAsync(caller.Account.Id, "handover", "set_share", PermissionScopes.Own, cancellationToken);
        if (!allowed)
        {
            throw new ForbiddenException("Chỉ quản lý hoặc quản trị viên mới được đặt tỷ lệ chia điểm cho người giới thiệu.");
        }
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
