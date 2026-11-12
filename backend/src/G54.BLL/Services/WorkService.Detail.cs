using System.Globalization;
using G54.BLL.Common.Constants;
using G54.BLL.Common.Exceptions;
using G54.BLL.Common.Extensions;
using G54.BLL.Dtos.Work;
using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.BLL.Services;

// Work detail screen (SCR-WRK-03): checklist, activity log, related KPIs, and the manager's plan edit.
public sealed partial class WorkService
{
    private const int MaxChecklistItems = 30;
    private const int MaxChecklistTitleLength = 200;
    private const int MaxPlanNoteLength = 1000;
    private const int MaxActivityMessageLength = 1500;
    private const int MaxActivities = 200;

    /// <summary>Work whose checklist the assignee can still tick.</summary>
    private static readonly WorkStatus[] ChecklistEditableStatuses =
        [WorkStatus.New, WorkStatus.InProgress, WorkStatus.Rejected, WorkStatus.Overdue];

    /// <summary>Work whose plan (deadline, priority, checklist) a manager can still change.</summary>
    private static readonly WorkStatus[] PlanEditableStatuses =
        [WorkStatus.New, WorkStatus.InProgress, WorkStatus.Rejected, WorkStatus.Overdue, WorkStatus.HandedOver];

    private sealed record DetailExtras(
        IReadOnlyList<WorkChecklistItemDto> Checklist,
        IReadOnlyList<WorkKpiDto> Kpis,
        string? AssignedByName,
        string? AssignedByPosition,
        bool CanEditPlan,
        bool CanUpdateChecklist);

    public async Task<IReadOnlyList<WorkActivityDto>?> GetActivitiesAsync(
        Guid id,
        Guid accountId,
        CancellationToken cancellationToken = default)
    {
        var caller = await ResolveCallerAsync(accountId, cancellationToken);
        var work = await FindVisibleWorkAsync(id, caller, cancellationToken);
        if (work is null)
        {
            return null;
        }

        return await unitOfWork.Repository<WorkActivity>().Query()
            .Where(a => a.WorkId == id)
            .OrderByDescending(a => a.CreatedAt)
            .Take(MaxActivities)
            .Select(a => new WorkActivityDto(
                a.Id,
                a.Action,
                a.Message,
                a.ActorEmployeeId,
                a.ActorEmployee != null ? a.ActorEmployee.FullName : null,
                a.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<WorkDetailDto> SetChecklistItemAsync(
        Guid id,
        Guid itemId,
        Guid userId,
        WorkChecklistItemStateDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);
        if (dto.IsDone is null)
        {
            throw new ValidationException("Trạng thái hoàn thành của mục kiểm tra là bắt buộc.");
        }

        var actor = await ResolveActorAsync(userId, cancellationToken);
        var work = await unitOfWork.Works.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("Công việc", id);

        if (work.AssigneeEmployeeId != actor.Id)
        {
            throw new ForbiddenException("Chỉ người được giao việc mới được cập nhật danh mục kiểm tra.");
        }

        if (!ChecklistEditableStatuses.Contains(work.Status))
        {
            throw new ConflictException(
                $"Không thể cập nhật danh mục kiểm tra khi công việc ở trạng thái {work.Status.ToEnumString()}.",
                ErrorCodes.WorkInvalidState);
        }

        var items = await unitOfWork.Repository<WorkChecklistItem>().Query(asNoTracking: false)
            .Where(c => c.WorkId == id)
            .ToListAsync(cancellationToken);
        var item = items.FirstOrDefault(c => c.Id == itemId)
            ?? throw new NotFoundException("Mục kiểm tra", itemId);

        var isDone = dto.IsDone.Value;
        if (item.IsDone != isDone)
        {
            var now = time.GetUtcNow();
            item.IsDone = isDone;
            item.DoneAt = isDone ? now : null;
            work.UpdatedAt = now;
            unitOfWork.Works.Update(work);
            await AddActivityAsync(
                work.Id,
                actor.Id,
                "CHECKLIST_UPDATED",
                $"Cập nhật danh mục kiểm tra {items.Count(c => c.IsDone)}/{items.Count}",
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return await LoadDetailForAsync(work.Id, userId, actor, cancellationToken);
    }

    public async Task<WorkDetailDto> UpdatePlanAsync(
        Guid id,
        Guid userId,
        UpdateWorkPlanDto dto,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var actor = await ResolveActorAsync(userId, cancellationToken);
        var work = await unitOfWork.Works.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException("Công việc", id);
        var assignee = await unitOfWork.Employees.GetByIdAsync(work.AssigneeEmployeeId, cancellationToken)
            ?? throw new NotFoundException("Nhân viên", work.AssigneeEmployeeId);

        if (!await CanEditPlanAsync(userId, actor.Id, actor.DepartmentId, assignee, cancellationToken))
        {
            throw new ForbiddenException("Bạn không có quyền sửa kế hoạch của công việc này.");
        }

        if (!PlanEditableStatuses.Contains(work.Status))
        {
            throw new ConflictException(
                $"Không thể sửa kế hoạch khi công việc ở trạng thái {work.Status.ToEnumString()}.",
                ErrorCodes.WorkInvalidState);
        }

        var newEnd = dto.PlannedEndDate ?? throw new ValidationException("Hạn hoàn thành là bắt buộc.");
        if (work.PlannedStartDate.HasValue && newEnd < work.PlannedStartDate.Value)
        {
            throw new ValidationException("Hạn hoàn thành không được nhỏ hơn ngày bắt đầu.");
        }

        if (work.BusinessCampaignId is { } campaignId)
        {
            var campaign = await unitOfWork.BusinessCampaigns.GetByIdAsync(campaignId, cancellationToken);
            if (campaign is not null && newEnd > campaign.EndDate)
            {
                throw new ValidationException(
                    $"Hạn hoàn thành ({FormatDate(newEnd)}) vượt quá ngày kết thúc chiến dịch ({FormatDate(campaign.EndDate)}).");
            }
        }

        var note = dto.Note?.Trim();
        if (note is { Length: > MaxPlanNoteLength })
        {
            throw new ValidationException($"Ghi chú không được vượt quá {MaxPlanNoteLength} ký tự.");
        }

        var newPriority = EnumApiFormat.ParseEnum<WorkPriority>(dto.Priority, "priority");
        var items = await unitOfWork.Repository<WorkChecklistItem>().Query(asNoTracking: false)
            .Where(c => c.WorkId == id)
            .ToListAsync(cancellationToken);

        var changes = new List<string>();

        if (work.PlannedEndDate != newEnd)
        {
            changes.Add(work.PlannedEndDate is { } oldEnd
                ? $"hạn {FormatDate(oldEnd)} → {FormatDate(newEnd)}"
                : $"hạn → {FormatDate(newEnd)}");
            work.PlannedEndDate = newEnd;
        }

        if (newPriority is { } priority && priority != work.Priority)
        {
            changes.Add($"ưu tiên {work.Priority.ToEnumString()} → {priority.ToEnumString()}");
            work.Priority = priority;
        }

        var checklistChanged = dto.Checklist is not null && await ApplyChecklistAsync(work.Id, items, dto.Checklist, cancellationToken);
        if (checklistChanged)
        {
            changes.Add("danh mục kiểm tra");
        }

        // An extended deadline takes an overdue work back to in progress: the date that made it overdue no longer applies.
        if (work.Status == WorkStatus.Overdue && newEnd >= time.GetBusinessToday())
        {
            work.Status = WorkStatus.InProgress;
            changes.Add("trạng thái Quá hạn → Đang thực hiện");
        }

        if (changes.Count == 0 && string.IsNullOrEmpty(note))
        {
            return await LoadDetailForAsync(work.Id, userId, actor, cancellationToken);
        }

        work.UpdatedAt = time.GetUtcNow();
        unitOfWork.Works.Update(work);

        var summary = changes.Count > 0 ? string.Join("; ", changes) : "chỉ có ghi chú";
        await AddActivityAsync(
            work.Id,
            actor.Id,
            "PLAN_UPDATED",
            string.IsNullOrEmpty(note) ? $"Sửa kế hoạch: {summary}" : $"Sửa kế hoạch: {summary}. Ghi chú: {note}",
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        await notifier.TryPublishAsync(
            [AutomaticNotifications.WorkPlanUpdated(work, assignee, actor, summary, note)],
            cancellationToken);

        return await LoadDetailForAsync(work.Id, userId, actor, cancellationToken);
    }

    /// <summary>
    /// Makes the stored checklist equal to the requested one. Done items must be listed unchanged; not-done items left
    /// out are removed; entries without an id are added. Returns whether anything changed.
    /// </summary>
    private async Task<bool> ApplyChecklistAsync(
        Guid workId,
        List<WorkChecklistItem> items,
        IReadOnlyList<WorkPlanChecklistItemDto> requested,
        CancellationToken cancellationToken)
    {
        if (requested.Count > MaxChecklistItems)
        {
            throw new ValidationException($"Danh mục kiểm tra tối đa {MaxChecklistItems} mục.");
        }

        var seen = new HashSet<Guid>();
        foreach (var entry in requested)
        {
            var title = entry?.Title?.Trim();
            if (string.IsNullOrEmpty(title) || title.Length > MaxChecklistTitleLength)
            {
                throw new ValidationException($"Tên mục kiểm tra là bắt buộc và tối đa {MaxChecklistTitleLength} ký tự.");
            }

            if (entry!.Id is { } entryId)
            {
                if (items.All(i => i.Id != entryId))
                {
                    throw new ValidationException("Mục kiểm tra không thuộc công việc này.");
                }

                if (!seen.Add(entryId))
                {
                    throw new ValidationException("Một mục kiểm tra bị liệt kê nhiều lần.");
                }
            }
        }

        foreach (var done in items.Where(i => i.IsDone))
        {
            var entry = requested.FirstOrDefault(r => r.Id == done.Id);
            if (entry is null || !string.Equals(entry.Title.Trim(), done.Title, StringComparison.Ordinal))
            {
                throw new ValidationException($"Mục đã xong không thể sửa hoặc xóa: {done.Title}.");
            }
        }

        var repository = unitOfWork.Repository<WorkChecklistItem>();
        var changed = false;

        var removed = items.Where(i => !i.IsDone && !seen.Contains(i.Id)).ToList();
        if (removed.Count > 0)
        {
            repository.RemoveRange(removed);
            changed = true;
        }

        var now = time.GetUtcNow();
        for (var index = 0; index < requested.Count; index++)
        {
            var entry = requested[index];
            var title = entry.Title.Trim();
            var existing = entry.Id is { } entryId ? items.First(i => i.Id == entryId) : null;
            if (existing is null)
            {
                await repository.AddAsync(
                    new WorkChecklistItem { Id = Guid.NewGuid(), WorkId = workId, Title = title, SortOrder = index, CreatedAt = now },
                    cancellationToken);
                changed = true;
                continue;
            }

            if (existing.Title != title || existing.SortOrder != index)
            {
                changed = true;
            }

            existing.Title = title;
            existing.SortOrder = index;
        }

        return changed;
    }

    /// <summary>The assignee's direct manager, or anyone whose <c>work:create</c> scope covers the assignee's department.</summary>
    private async Task<bool> CanEditPlanAsync(
        Guid accountId,
        Guid viewerEmployeeId,
        Guid viewerDepartmentId,
        Employee assignee,
        CancellationToken cancellationToken)
    {
        if (viewerEmployeeId == assignee.Id)
        {
            return false;
        }

        if (assignee.ManagerId == viewerEmployeeId)
        {
            return true;
        }

        var granted = await permissions.GetEffectivePermissionsAsync(accountId, cancellationToken);
        var level = AccessScope.LevelFor(granted, "work", "create");
        if (level < AccessScope.DeptLevel)
        {
            return false;
        }

        if (level >= AccessScope.AllLevel)
        {
            return true;
        }

        var hierarchy = await DepartmentHierarchy.LoadAsync(unitOfWork.Departments.Query(), cancellationToken);
        var inScope = AccessScope.DepartmentsInScope(level, viewerDepartmentId, hierarchy);
        return inScope is null || inScope.Contains(assignee.DepartmentId);
    }

    /// <summary>The work if it exists and lies inside the caller's <c>work:read</c> scope; otherwise null.</summary>
    private async Task<Work?> FindVisibleWorkAsync(Guid id, Caller caller, CancellationToken cancellationToken)
    {
        var work = await unitOfWork.Works.GetWithDetailsAsync(id, cancellationToken);
        if (work is null)
        {
            return null;
        }

        var assignee = work.AssigneeEmployee;
        var isAssigneeOrManager = work.AssigneeEmployeeId == caller.EmployeeId || assignee?.ManagerId == caller.EmployeeId;
        var hierarchy = await DepartmentHierarchy.LoadAsync(unitOfWork.Departments.Query(), cancellationToken);
        var visibleDepartments = AccessScope.DepartmentsInScope(caller.Level, caller.DepartmentId, hierarchy);
        var inScope = caller.Level >= AccessScope.AllLevel
            || isAssigneeOrManager
            || (assignee is not null && visibleDepartments is not null && visibleDepartments.Contains(assignee.DepartmentId));

        return inScope ? work : null;
    }

    /// <summary>The detail as the given viewer sees it (checklist, KPIs, who assigned it, what they may do).</summary>
    private async Task<WorkDetailDto> BuildDetailAsync(
        Work work,
        Guid accountId,
        Guid viewerEmployeeId,
        Guid viewerDepartmentId,
        CancellationToken cancellationToken)
    {
        var checklist = await unitOfWork.Repository<WorkChecklistItem>().Query()
            .Where(c => c.WorkId == work.Id)
            .OrderBy(c => c.SortOrder)
            .Select(c => new WorkChecklistItemDto(c.Id, c.Title, c.IsDone, c.SortOrder, c.DoneAt))
            .ToListAsync(cancellationToken);

        var kpis = new List<WorkKpiDto>();
        if (work.WorkTypeId is { } workTypeId)
        {
            var today = time.GetBusinessToday();
            var links = await unitOfWork.WorkTypeKpis.Query()
                .Where(k => k.WorkTypeId == workTypeId && k.EffectiveFrom <= today && (k.EffectiveTo == null || k.EffectiveTo >= today))
                .Include(k => k.KpiDefinition)
                .OrderByDescending(k => k.IsRequired)
                .ThenBy(k => k.KpiDefinition.Code)
                .ToListAsync(cancellationToken);
            kpis.AddRange(links.Select(k => new WorkKpiDto(
                k.KpiDefinition.Code,
                k.KpiDefinition.Name,
                k.KpiDefinition.CreditRole.ToString().ToUpperInvariant())));
        }

        var assignedBy = await unitOfWork.Employees.Query()
            .Where(e => e.Id == work.CreatedBy)
            .Select(e => new { e.FullName, PositionTitle = e.JobPosition.Title })
            .FirstOrDefaultAsync(cancellationToken);

        var assignee = work.AssigneeEmployee
            ?? await unitOfWork.Employees.GetByIdAsync(work.AssigneeEmployeeId, cancellationToken);
        var canEditPlan = assignee is not null
            && PlanEditableStatuses.Contains(work.Status)
            && await CanEditPlanAsync(accountId, viewerEmployeeId, viewerDepartmentId, assignee, cancellationToken);
        var canUpdateChecklist = viewerEmployeeId == work.AssigneeEmployeeId && ChecklistEditableStatuses.Contains(work.Status);

        return MapToDetail(
            work,
            work.WorkType?.Name,
            work.BusinessCampaign?.Name,
            assignee?.FullName,
            assignee?.EmployeeCode,
            new DetailExtras(checklist, kpis, assignedBy?.FullName, assignedBy?.PositionTitle, canEditPlan, canUpdateChecklist));
    }

    /// <summary>Reloads the work and builds its detail for an already resolved actor.</summary>
    private async Task<WorkDetailDto> LoadDetailForAsync(Guid id, Guid accountId, Employee actor, CancellationToken cancellationToken)
    {
        var work = await unitOfWork.Works.GetWithDetailsAsync(id, cancellationToken)
            ?? throw new NotFoundException("Công việc", id);
        return await BuildDetailAsync(work, accountId, actor.Id, actor.DepartmentId, cancellationToken);
    }

    private async Task AddActivityAsync(Guid workId, Guid? actorId, string action, string message, CancellationToken cancellationToken)
    {
        await unitOfWork.Repository<WorkActivity>().AddAsync(
            new WorkActivity
            {
                Id = Guid.NewGuid(),
                WorkId = workId,
                ActorEmployeeId = actorId,
                Action = action,
                Message = message.Length > MaxActivityMessageLength ? message[..MaxActivityMessageLength] : message,
                CreatedAt = time.GetUtcNow(),
            },
            cancellationToken);
    }

    private static string FormatDate(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
