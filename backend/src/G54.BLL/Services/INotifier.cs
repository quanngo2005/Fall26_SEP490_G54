using G54.DAL.Entities;

namespace G54.BLL.Services;

/// <summary>One notification to create for one recipient.</summary>
public sealed record NotificationDraft(
    Guid RecipientEmployeeId,
    NotificationType Type,
    string Title,
    string? Body = null,
    NotificationSeverity Severity = NotificationSeverity.Normal,
    NotificationRelatedType? RelatedType = null,
    Guid? RelatedId = null,
    Guid? SenderEmployeeId = null);

/// <summary>Stores notifications and pushes them to the recipients who are online.</summary>
public interface INotifier
{
    /// <summary>Stores the notifications, then pushes them. A failure to store is thrown; a failure to push is only logged.</summary>
    Task PublishAsync(IReadOnlyCollection<NotificationDraft> drafts, CancellationToken cancellationToken = default);

    /// <summary>
    /// For notifications raised as a side effect of other work: a failure is logged instead of thrown, so it never
    /// undoes the work that has already been saved.
    /// </summary>
    Task TryPublishAsync(IReadOnlyCollection<NotificationDraft> drafts, CancellationToken cancellationToken = default);
}
