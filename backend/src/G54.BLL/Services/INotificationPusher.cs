using G54.BLL.Dtos.Notification;

namespace G54.BLL.Services;

/// <summary>Delivers a stored notification to the employee's open sessions (SignalR in the API).</summary>
public interface INotificationPusher
{
    Task PushAsync(Guid employeeId, NotificationDto notification, CancellationToken cancellationToken = default);
}
