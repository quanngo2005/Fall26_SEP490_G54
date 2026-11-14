using System.Globalization;
using G54.DAL.Entities;

namespace G54.BLL.Services;

/// <summary>The notifications the system raises by itself, with the Vietnamese text each one shows.</summary>
internal static class AutomaticNotifications
{
    /// <summary>An employee asked to hand a work over: the recipient is told, and so is the requester's manager, who can settle it.</summary>
    public static List<NotificationDraft> HandoverRequested(TaskHandover handover, Work work, Employee from, Employee to)
    {
        var drafts = new List<NotificationDraft>
        {
            ForHandover(
                to.Id,
                NotificationType.HandoverRequested,
                "Yêu cầu bàn giao công việc",
                $"{from.FullName} muốn bàn giao công việc {Describe(work)} cho bạn. Hãy chấp nhận hoặc từ chối.",
                NotificationSeverity.Important,
                handover),
        };

        if (from.ManagerId is { } managerId && managerId != to.Id)
        {
            drafts.Add(ForHandover(
                managerId,
                NotificationType.HandoverRequested,
                "Yêu cầu bàn giao công việc",
                $"{from.FullName} muốn bàn giao công việc {Describe(work)} cho {to.FullName}.",
                NotificationSeverity.Normal,
                handover));
        }

        return drafts;
    }

    /// <summary>A handover was settled: whoever did not make the decision hears the outcome.</summary>
    public static List<NotificationDraft> HandoverDecided(TaskHandover handover, Work work, Employee from, Employee to, Guid deciderId)
    {
        var drafts = new List<NotificationDraft>();
        var accepted = handover.Status == HandoverStatus.Accepted;
        var details = $"{Describe(work)} từ {from.FullName} sang {to.FullName}";

        if (from.Id != deciderId)
        {
            drafts.Add(accepted
                ? ForHandover(from.Id, NotificationType.HandoverAccepted, "Yêu cầu bàn giao đã được chấp nhận", $"Công việc {details} đã được chuyển.", NotificationSeverity.Normal, handover)
                : ForHandover(from.Id, NotificationType.HandoverRejected, "Yêu cầu bàn giao bị từ chối", $"Yêu cầu bàn giao công việc {details} đã bị từ chối.", NotificationSeverity.Normal, handover));
        }

        if (to.Id != deciderId)
        {
            drafts.Add(accepted
                ? ForHandover(to.Id, NotificationType.HandoverTransferred, "Bạn được giao công việc mới", $"Công việc {details} đã được chuyển cho bạn.", NotificationSeverity.Important, handover)
                : ForHandover(to.Id, NotificationType.HandoverRejected, "Yêu cầu bàn giao bị từ chối", $"Yêu cầu bàn giao công việc {details} đã bị quản lý từ chối.", NotificationSeverity.Normal, handover));
        }

        return drafts;
    }

    /// <summary>A work passed its deadline: the assignee is told, and so is their direct manager.</summary>
    public static List<NotificationDraft> WorkOverdue(Work work, Employee assignee)
    {
        var due = work.PlannedEndDate?.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var drafts = new List<NotificationDraft>
        {
            ForWork(assignee.Id, "Công việc quá hạn", $"Công việc {Describe(work)} đã quá hạn (hạn {due}).", NotificationSeverity.Important, work),
        };

        if (assignee.ManagerId is { } managerId)
        {
            drafts.Add(ForWork(managerId, "Nhân viên có công việc quá hạn", $"{assignee.FullName} có công việc {Describe(work)} đã quá hạn (hạn {due}).", NotificationSeverity.Normal, work));
        }

        return drafts;
    }

    /// <summary>A manager changed the plan of a work: the assignee is told what changed and any note.</summary>
    public static NotificationDraft WorkPlanUpdated(Work work, Employee assignee, Employee manager, string summary, string? note)
    {
        var body = $"{manager.FullName} đã sửa kế hoạch công việc {Describe(work)}: {summary}.";
        if (!string.IsNullOrWhiteSpace(note))
        {
            body += $" Ghi chú: {note}";
        }

        return new NotificationDraft(
            assignee.Id,
            NotificationType.WorkPlanUpdated,
            "Kế hoạch công việc được cập nhật",
            body.Length > 2000 ? body[..2000] : body,
            NotificationSeverity.Important,
            NotificationRelatedType.Work,
            work.Id);
    }

    private static string Describe(Work work) => $"{work.Code} - {work.Title}";

    private static NotificationDraft ForHandover(Guid recipientId, NotificationType type, string title, string body, NotificationSeverity severity, TaskHandover handover)
        => new(recipientId, type, title, body, severity, NotificationRelatedType.Handover, handover.Id);

    private static NotificationDraft ForWork(Guid recipientId, string title, string body, NotificationSeverity severity, Work work)
        => new(recipientId, NotificationType.WorkOverdue, title, body, severity, NotificationRelatedType.Work, work.Id);
}
