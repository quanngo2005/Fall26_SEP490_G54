using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Handover;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

/// <summary>The direct manager of the requester settles a pending handover without the recipient's say.</summary>
public sealed partial class HandoverService
{
    private enum ManagerAction
    {
        Approve,
        Reassign,
        Reject,
    }

    public Task<HandoverDetailDto> ApproveByManagerAsync(Guid handoverId, Guid userId, CancellationToken cancellationToken = default)
        => ManagerDecideAsync(handoverId, userId, ManagerAction.Approve, null, cancellationToken);

    public Task<HandoverDetailDto> ReassignByManagerAsync(
        Guid handoverId,
        Guid userId,
        ReassignHandoverRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ToEmployeeId is null || request.ToEmployeeId == Guid.Empty)
        {
            throw new ValidationException("Người nhận là bắt buộc.");
        }

        return ManagerDecideAsync(handoverId, userId, ManagerAction.Reassign, request.ToEmployeeId, cancellationToken);
    }

    public Task<HandoverDetailDto> RejectByManagerAsync(Guid handoverId, Guid userId, CancellationToken cancellationToken = default)
        => ManagerDecideAsync(handoverId, userId, ManagerAction.Reject, null, cancellationToken);

    private async Task<HandoverDetailDto> ManagerDecideAsync(
        Guid handoverId,
        Guid userId,
        ManagerAction action,
        Guid? newRecipientId,
        CancellationToken cancellationToken)
    {
        var caller = await ResolveCallerAsync(userId, cancellationToken);

        var handover = await db.TaskHandovers
            .Include(h => h.Work)
            .Include(h => h.FromEmployee)
            .Include(h => h.ToEmployee)
            .FirstOrDefaultAsync(h => h.Id == handoverId, cancellationToken)
            ?? throw new NotFoundException("Yêu cầu bàn giao", handoverId);

        if (handover.FromEmployee.ManagerId != caller.Id)
        {
            throw new ForbiddenException("Chỉ quản lý trực tiếp của người bàn giao mới có quyền xử lý yêu cầu này.");
        }

        if (handover.Status != HandoverStatus.Pending)
        {
            throw new ConflictException(
                ErrorCodes.GetDefaultMessage(ErrorCodes.HandoverNotPending),
                ErrorCodes.HandoverNotPending);
        }

        var now = time.GetUtcNow();
        var work = handover.Work;
        if (action == ManagerAction.Reject)
        {
            handover.Status = HandoverStatus.Rejected;
        }
        else
        {
            if (action == ManagerAction.Reassign)
            {
                await ReassignRecipientAsync(handover, work, newRecipientId!.Value, cancellationToken);
            }

            AcceptInto(handover, work, now);
            handover.ApprovedBy = caller.Id;
        }

        handover.UpdatedAt = now;
        await SaveDecisionAsync(cancellationToken);

        LogHandoverDecided(logger, $"MANAGER_{action.ToString().ToUpperInvariant()}", handover.Id, work.Id, handover.FromEmployeeId, handover.ToEmployeeId, null);

        return Map(handover, work, handover.FromEmployee, handover.ToEmployee);
    }

    private async Task ReassignRecipientAsync(TaskHandover handover, Work work, Guid newRecipientId, CancellationToken cancellationToken)
    {
        if (newRecipientId == work.AssigneeEmployeeId)
        {
            throw new ValidationException("Không thể bàn giao công việc cho người đang phụ trách nó.");
        }

        var recipient = await db.Employees
            .FirstOrDefaultAsync(e => e.Id == newRecipientId, cancellationToken)
            ?? throw new NotFoundException("Nhân viên nhận bàn giao", newRecipientId);
        if (recipient.Status != EmployeeStatus.Active)
        {
            throw new ValidationException("Nhân viên nhận bàn giao không còn hoạt động.");
        }

        handover.ToEmployee = recipient;
        handover.ToEmployeeId = recipient.Id;
    }
}
