using System.Globalization;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Dtos.Handover;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace G54.BLL.Services;

/// <summary>Manager actions (approve, reassign/assign from pool, reject) and administrator settings for handovers.</summary>
public sealed partial class HandoverService
{
    private enum ManagerAction
    {
        Approve,
        Reassign,
        Reject,
    }

    public async Task<IReadOnlyList<HandoverDetailDto>> GetPoolAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(userId, cancellationToken);
        var items = await db.TaskHandovers.AsNoTracking()
            .Include(h => h.Work)
            .Include(h => h.FromEmployee)
            .Include(h => h.ToEmployee)
            .Where(h => h.Status == HandoverStatus.InPool && h.FromEmployee.ManagerId == caller.Id)
            .OrderBy(h => h.HandoverAt)
            .ToListAsync(cancellationToken);

        return items.Select(h => Map(h, h.Work, h.FromEmployee, h.ToEmployee)).ToList();
    }

    public async Task<HandoverSettingsDto> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.SystemSettings.AsNoTracking()
            .Where(s => s.Key == SettingKeyAssignmentMode || s.Key == SettingKeyMinProgressPercent)
            .ToListAsync(cancellationToken);

        var modeRow = rows.FirstOrDefault(s => s.Key == SettingKeyAssignmentMode);
        var progressRow = rows.FirstOrDefault(s => s.Key == SettingKeyMinProgressPercent);

        var mode = modeRow?.Value
            ?? configuration.GetValue("Handover:TransferAssignmentMode", ModeRandom)
            ?? ModeRandom;
        if (!string.Equals(mode, ModeRandom, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(mode, ModeAssigned, StringComparison.OrdinalIgnoreCase))
        {
            mode = ModeRandom;
        }
        else
        {
            mode = mode.ToUpperInvariant();
        }

        var minProgress = progressRow is not null
            && decimal.TryParse(progressRow.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : configuration.GetValue("Handover:TransferMinProgressPercent", DefaultTransferMinProgressPercent);

        DateTimeOffset? updatedAt = rows.Count > 0 ? rows.Max(r => r.UpdatedAt) : null;
        return new HandoverSettingsDto(mode, minProgress, updatedAt);
    }

    public async Task<HandoverSettingsDto> UpdateSettingsAsync(
        UpdateHandoverSettingsRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new List<string>();
        var rawMode = request.TransferAssignmentMode?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(rawMode) || (rawMode != ModeRandom && rawMode != ModeAssigned))
        {
            errors.Add($"Chế độ phân công chuyển việc phải là '{ModeRandom}' hoặc '{ModeAssigned}'.");
        }

        if (request.TransferMinProgressPercent is null)
        {
            errors.Add("Ngưỡng tiến độ tối thiểu là bắt buộc.");
        }
        else if (request.TransferMinProgressPercent is < 0 or > 100)
        {
            errors.Add("Ngưỡng tiến độ tối thiểu phải nằm trong khoảng 0-100.");
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var now = time.GetUtcNow();
        var minProgress = Math.Round(request.TransferMinProgressPercent!.Value, 2);
        await UpsertSettingAsync(SettingKeyAssignmentMode, rawMode!, now, cancellationToken);
        await UpsertSettingAsync(SettingKeyMinProgressPercent, minProgress.ToString("0.##", CultureInfo.InvariantCulture), now, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        return new HandoverSettingsDto(rawMode!, minProgress, now);
    }

    private async Task UpsertSettingAsync(string key, string value, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var existing = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
        if (existing is null)
        {
            db.SystemSettings.Add(new SystemSetting
            {
                Key = key,
                Value = value,
                UpdatedAt = now,
            });
        }
        else
        {
            existing.Value = value;
            existing.UpdatedAt = now;
        }
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

        if (handover.Status is not (HandoverStatus.Pending or HandoverStatus.InPool))
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
            else if (handover.ToEmployeeId is null)
            {
                throw new ValidationException("Yêu cầu trong hàng chờ (pool) chưa có người nhận; vui lòng chỉ định người nhận.");
            }

            AcceptInto(handover, work, now);
            handover.ApprovedBy = caller.Id;
        }

        handover.UpdatedAt = now;
        await SaveDecisionAsync(cancellationToken);

        LogHandoverDecided(logger, $"MANAGER_{action.ToString().ToUpperInvariant()}", handover.Id, work.Id, handover.FromEmployeeId, handover.ToEmployeeId, null);
        await notifier.TryPublishAsync(AutomaticNotifications.HandoverDecided(handover, work, handover.FromEmployee, handover.ToEmployee, caller.Id), cancellationToken);

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
