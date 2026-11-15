using System.ComponentModel.DataAnnotations;

namespace G54.BLL.Dtos.Work;

/// <summary>One line of a work's checklist. Progress of the work is the share of done items.</summary>
public sealed record WorkChecklistItemDto(Guid Id, string Title, bool IsDone, int SortOrder, DateTimeOffset? DoneAt);

/// <summary>A KPI the work's type feeds; <c>CreditRole</c> is <c>PROCESSOR</c> or <c>REFERRER</c>.</summary>
public sealed record WorkKpiDto(string Code, string Name, string CreditRole);

/// <summary>One entry of the activity log, newest first in the list. <c>ActorName</c> is null for system entries.</summary>
public sealed record WorkActivityDto(Guid Id, string Action, string Message, Guid? ActorEmployeeId, string? ActorName, DateTimeOffset CreatedAt);

/// <summary><c>PATCH /works/{id}/checklist-items/{itemId}</c> body.</summary>
public sealed record WorkChecklistItemStateDto
{
    [Required(ErrorMessage = "Trạng thái hoàn thành của mục kiểm tra là bắt buộc.")]
    public bool? IsDone { get; init; }
}

/// <summary>A checklist line in <see cref="UpdateWorkPlanDto"/>: an existing item keeps its <c>Id</c>, a new one has none.</summary>
public sealed record WorkPlanChecklistItemDto
{
    public Guid? Id { get; init; }

    [Required(ErrorMessage = "Tên mục kiểm tra là bắt buộc.")]
    [StringLength(200, ErrorMessage = "Tên mục kiểm tra tối đa 200 ký tự.")]
    public string Title { get; init; } = string.Empty;
}

/// <summary><c>PUT /works/{id}/plan</c> body (SCR-WRK-03-M1). Only what the manager may change after assigning.</summary>
public sealed record UpdateWorkPlanDto
{
    /// <summary>The new deadline (<c>YYYY-MM-DD</c>); required.</summary>
    [Required(ErrorMessage = "Hạn hoàn thành là bắt buộc.")]
    public DateOnly? PlannedEndDate { get; init; }

    /// <summary><c>Low | Medium | High | Critical</c>; null keeps the current priority.</summary>
    public string? Priority { get; init; }

    /// <summary>
    /// The complete checklist in display order; null keeps it as is. Items that are done must be listed unchanged,
    /// not-done items that are left out are removed, entries without an <c>Id</c> are added.
    /// </summary>
    public IReadOnlyList<WorkPlanChecklistItemDto>? Checklist { get; init; }

    /// <summary>A note for the assignee, kept in the activity log and the notification; at most 1000 characters.</summary>
    [StringLength(1000, ErrorMessage = "Ghi chú không được vượt quá 1000 ký tự.")]
    public string? Note { get; init; }
}
