using G54.BLL.Common.Models;
using G54.BLL.Dtos.Notification;

namespace G54.BLL.Services;

public interface INotificationService
{
    /// <summary>Sends an announcement to the chosen employees, or to everyone in the sender's <c>notification:send</c> scope.</summary>
    Task<SendNotificationResultDto> SendAsync(Guid userId, SendNotificationDto request, CancellationToken cancellationToken = default);

    /// <summary>The caller's own notifications, newest first.</summary>
    Task<PagedResult<NotificationDto>> GetMineAsync(Guid userId, NotificationFilterDto filter, CancellationToken cancellationToken = default);

    Task<NotificationCountDto> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Marks one of the caller's notifications as read; reading it again changes nothing.</summary>
    Task<NotificationDto> MarkReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken = default);

    /// <summary>Marks every unread notification of the caller as read and returns how many it changed.</summary>
    Task<NotificationCountDto> MarkAllReadAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>The employee behind a signed-in account; the push channel addresses notifications by employee.</summary>
    Task<Guid> GetEmployeeIdAsync(Guid userId, CancellationToken cancellationToken = default);
}
