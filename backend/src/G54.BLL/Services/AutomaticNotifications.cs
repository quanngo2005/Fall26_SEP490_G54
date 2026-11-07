using System.Globalization;
using G54.DAL.Entities;

namespace G54.BLL.Services;

/// <summary>The notifications the system raises by itself, with the Vietnamese text each one shows.</summary>
internal static class AutomaticNotifications
{
    /// <summary>A work was handed over: the recipient is told, and so is the previous assignee unless they handed it over themselves.</summary>
    public static List<NotificationDraft> HandoverTransferred(TaskHandover handover, Work work, Employee from, Employee to, Guid callerId)
    {
        var drafts = new List<NotificationDraft>();
        var details = $"{Describe(work)} từ {from.FullName} sang {to.FullName}";

        if (from.Id != callerId)
        {
            drafts.Add(ForHandover(from.Id, NotificationType.HandoverTransferred, "Công việc đã được bàn giao", $"Công việc {details} đã được chuyển.", NotificationSeverity.Normal, handover));
        }

        drafts.Add(ForHandover(to.Id, NotificationType.HandoverTransferred, "Bạn được giao công việc mới", $"Công việc {details} đã được chuyển cho bạn.", NotificationSeverity.Important, handover));

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

    private static string Describe(Work work) => $"{work.Code} - {work.Title}";

    private static NotificationDraft ForHandover(Guid recipientId, NotificationType type, string title, string body, NotificationSeverity severity, TaskHandover handover)
        => new(recipientId, type, title, body, severity, NotificationRelatedType.Handover, handover.Id);

    private static NotificationDraft ForWork(Guid recipientId, string title, string body, NotificationSeverity severity, Work work)
        => new(recipientId, NotificationType.WorkOverdue, title, body, severity, NotificationRelatedType.Work, work.Id);
}
